﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc;
using Opc.Da;
using OpcDAToMSA.modbus;
using OpcDAToMSA.utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace OPCDA2MSA.opc
{
    public class OpcNet
    {

        private readonly OpcCom.Factory fact = new OpcCom.Factory();

        private Opc.Da.Server server = null;

        public List<Item> items = new List<Item>();

        private readonly CfgJson cfg = Config.GetConfig();

        //定义枚举基于COM服务器的接口，用来搜索所有的此类服务器。
        private readonly IDiscovery discovery = new OpcCom.ServerEnumerator();

        //选择性的浏览地址空间。
        private readonly BrowseFilters filters = new BrowseFilters
        {
            ReturnAllProperties = true, //获取数据项的属性
            ReturnPropertyValues = true, //要求返回属性的值
        };

        private readonly ModbusTcp modbusTcp = new ModbusTcp();

        private readonly MsaTcp msaTcp = new MsaTcp();

        public OpcNet()
        {
            modbusTcp.Run();
            //msaTcp.Run();
        }

        // 连接OPC服务器
        public void Connect()
        {
            string host = cfg.Opcda.Host;
            string node = cfg.Opcda.Server;

            Opc.Server[] servers = discovery.GetAvailableServers(Specification.COM_DA_20);
            //Console.WriteLine(JsonConvert.SerializeObject(servers));
            if (servers != null && servers.Length > 0)
            {
                for (int i = 0; i < servers.Length; i++)
                {
                    if (servers[i] != null)
                    {
                        Console.WriteLine(servers[i].Name);
                        //Console.WriteLine(JsonConvert.SerializeObject(servers[i]));
                    }
                }
            }
            //Opc.URL url = new Opc.URL("opcda://localhost/BECKHOFF.TwinCATOpcServerDA");
            URL url = new URL($@"opcda://{host}/{node}");
            server = new Opc.Da.Server(fact, url);
            try
            {
                server.Connect(url, new ConnectData(new System.Net.NetworkCredential(cfg.Opcda.Username, cfg.Opcda.Password)));
                //server.Connect();
                Console.WriteLine($@"OPC Server {node} is connected");
                SetItems();
                for (int i = 0; i < items.Count; i++)
                {
                    Console.WriteLine(items[i].ItemName);
                }
                ModbusTcp();
                //MsaTcp();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public void MsaTcp()
        {
            while (true)
            {
                //Console.Clear();
                try
                {
                    //Console.ForegroundColor = ConsoleColor.Green;
                    ItemValueResult[] values = server.Read(items.ToArray());
                    if (values != null && values.Length > 0)
                    {
                        msaTcp.Send(values);
                    }
                }
                catch (Exception ex)
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.Message);
                    //throw;
                }
                Thread.Sleep(cfg.Msa.Interval);
            }
        }

        public void ModbusTcp()
        {
            int[] regs = cfg.Modbus.Slave.Registers.ToArray();
            while (true)
            {
                //Console.Clear();
                try
                {
                    //Console.ForegroundColor = ConsoleColor.Green;
                    var values = server.Read(items.ToArray());
                    if (values != null && values.Length > 0)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (values[i] != null)
                            {
                                int index = regs[i];
                                System.Type type = values[i].Value.GetType();
                                //Console.WriteLine(type);
                                Console.WriteLine($@"{values[i].ItemName}@{index}={values[i].Value} {type}");

                                switch (values[i].Value.GetType().ToString())
                                {
                                    case "System.Boolean":
                                        modbusTcp.Store.HoldingRegisters[index] = BitConverter.ToUInt16(ConvertUtil.BoolToBytes((bool)values[i].Value),0);
                                        break;
                                    //case "System.Int16":
                                    //    modbusTcp.Store.HoldingRegisters[index] = BitConverter.ToUInt16(BitConverter.GetBytes((short)values[i].Value), 0);
                                    //    break;
                                        //case "System.Int32":
                                        //    modbusTcp.Store.HoldingRegisters[index] = BitConverter.ToUInt16(BitConverter.GetBytes((uint)values[i].Value), 0);
                                        //    modbusTcp.Store.HoldingRegisters[index+1] = BitConverter.ToUInt16(BitConverter.GetBytes((uint)values[i].Value), 2);
                                        //    break;
                                }

                                //byte[] bytes = ConvertUtil.getByte(values[i].Value);
                                //Console.WriteLine($@"{BitConverter.ToString(bytes)}@{bytes.Length}");
                    
                                //if (bytes.Length > 2) { 
                                //    modbusTcp.Store.HoldingRegisters[index+1] = BitConverter.ToUInt16(bytes, 2);
                                //}
                                //BitConverter.ToUInt16((float)values[i].Value,0);
                                //modbusTcp.Store.HoldingRegisters[index] = BitConverter.ToUInt16(, 0);
                                //modbusTcp.Store.HoldingRegisters[index + 1] = BitConverter.ToUInt16(ConvertUtil.ObjectToBytes(values[i].Value), 2);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex.Message);
                    //throw;
                }
                Thread.Sleep(cfg.Opcda.Interval);
            }
        }

        public void Subscription()
        {
            //设定组状态
            var state = new SubscriptionState();//组（订阅者）状态，相当于OPC规范中组的参数
            state.Name = "Group0";//组名
            state.ServerHandle = null;//服务器给该组分配的句柄。
            state.ClientHandle = Guid.NewGuid().ToString();//客户端给该组分配的句柄。
            state.Active = true;//激活该组。
            state.UpdateRate = 100;//刷新频率为1秒。
            state.Deadband = 0;// 死区值，设为0时，服务器端该组内任何数据变化都通知组。
            state.Locale = null;//不设置地区值。

            //添加组
            var subscription = (Subscription)server.CreateSubscription(state);//创建组

            //添加Item
            subscription.AddItems(items.ToArray());

            //注册回调事件
            subscription.DataChanged += new DataChangedEventHandler(OnDataChange);

            //以下测试同步读
            //以下读整个组
            //ItemValueResult[] values = subscription.Read(subscription.Items);

            //以下遍历读到的全部值
            //foreach (ItemValueResult value in values)
            //{
            //    Console.WriteLine("同步读：ItemName:{0}, Value:{1}, Quality:{2}, Timestamp:{3}", value.ItemName, value.Value, value.Quality, value.Timestamp);
            //}

            //以下测试异步读
            subscription.Read(subscription.Items, 1, OnReadComplete, out IRequest quest);
        }

        //DataChange回调
        public void OnDataChange(object subscriptionHandle, object requestHandle, ItemValueResult[] values)
        {
            foreach (ItemValueResult value in values)
            {
                Console.WriteLine("OnDataChange：ItemName:{0}, Value:{1}, Quality:{2}, Timestamp:{3}", value.ItemName, value.Value, value.Quality, value.Timestamp);
            }
            Console.WriteLine("事件信号句柄为：{0}", requestHandle);
        }

        //ReadComplete回调
        public void OnReadComplete(object requestHandle, ItemValueResult[] values)
        {
            foreach (ItemValueResult value in values)
            {
                Console.WriteLine("异步读：ItemName:{0}, Value:{1}, Quality:{2}, Timestamp:{3}", value.ItemName, value.Value, value.Quality, value.Timestamp);
            }
            Console.WriteLine("事件信号句柄为：{0}", requestHandle);
        }

        private void SetItems()
        {
            TreeNode node = new TreeNode(server.Name);
            BrowseAddress(node, null);//浏览根节点所包括的子项BrowseElement。过程Browse下文列出。
        }

        private void BrowseAddress(TreeNode node, BrowseElement parent)
        {//递归函数，浏览parent下所有的数据项，将这些项显示在控件TreeView的node节点下。
            if (parent != null && parent.IsItem == true)
                return;//如果BrowseElement对象是Item，则说明是组合的最后一级，终止递归。
            try
            {
                ItemIdentifier itemID = null;//BrowseElement和Item共同的父类。
                if (node.Tag != null && node.Tag.GetType() == typeof(BrowseElement))
                {//该节点是BrowseElement对象，而不是根节点。
                    parent = (BrowseElement)node.Tag;
                    itemID = new ItemIdentifier(parent.ItemPath, parent.ItemName);
                }
                BrowsePosition position = null;//地址空间巨大，则需要此使用此对象，一般不用。
                BrowseElement[] elements = server.Browse(itemID, filters, out position);
                if (elements != null)
                {//浏览到服务器m_server对应itemID所包含的元素。
                    foreach (BrowseElement element in elements)
                    {
                        if (element.IsItem == true)
                        {
                            if (cfg.Opcda.Items != null && cfg.Opcda.Items.Count > 0)
                            {
                                //定义Item列表
                                string[] keys = cfg.Opcda.Items.ToArray();
                                if (keys.Contains(element.ItemName.ToString()))
                                {
                                    items.Add(new Item
                                    {
                                        ClientHandle = Guid.NewGuid().ToString(),//客户端给该数据项分配的句柄。
                                        ItemPath = element.ItemPath, //该数据项在服务器中的路径。
                                        ItemName = element.ItemName //该数据项在服务器中的名字。
                                    });
                                }
                            }
                            else
                            {
                                items.Add(new Item
                                {
                                    ClientHandle = Guid.NewGuid().ToString(),//客户端给该数据项分配的句柄。
                                    ItemPath = element.ItemPath, //该数据项在服务器中的路径。
                                    ItemName = element.ItemName //该数据项在服务器中的名字。
                                });
                            }
                        }
                        TreeNode newnode = AddBrowseElement(node, element);//加入到TreeView
                        BrowseAddress(newnode, element);//递归调用
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        //将浏览到的BrowseElement对象加入到控件TreeView中。
        private TreeNode AddBrowseElement(TreeNode previou, BrowseElement element)
        {
            TreeNode node = new TreeNode(element.Name);
            node.Tag = element;//将BrowseElement对象记录到节点。
            previou.Nodes.Add(node);//将节点加入到TreeView中。
            return node;// 返回node,由递归函数使用。
        }
    }


}
