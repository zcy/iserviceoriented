﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace IServiceOriented.ServiceBus.Samples.Chat
{
    public class ChatClient
    {
        public ChatClient(string name, bool version2)
        {
            _from = name;

            if (version2)
            {
                _host = new ServiceHost(new IncomingHandler2(), new Uri("net.pipe://localhost/chat/" + _from));
            }
            else
            {
                _host = new ServiceHost(new IncomingHandler(), new Uri("net.pipe://localhost/chat/" + _from));
            }

            _version2 = version2;
        }

        bool _version2;

        public void Start()
        {
            _host.Open();

            Service.Use<IServiceBusManagementService>(serviceBus =>
             {
                    if (_version2)
                    {
                        serviceBus.Subscribe(new SubscriptionEndpoint(Guid.NewGuid(), _from, "ChatClient2", "net.pipe://localhost/chat/" + _from + "/send", typeof(IChatService2), new WcfDispatcher(), new ChatFilter2(_from)));                        
                    }
                    else
                    {
                        serviceBus.Subscribe(new SubscriptionEndpoint(Guid.NewGuid(), _from, "ChatClient", "net.pipe://localhost/chat/" + _from + "/send", typeof(IChatService), new WcfDispatcher(), new ChatFilter(_from)));
                    }
                });
        }

        string _from;   

        
        public void Send(string to, string message)
        {
            if (_version2)
            {
                Service.Use<IChatService2>("ChatClient2", chatService =>
                {
                    chatService.SendMessage(new SendMessageRequest2("Title", _from, to, message));
                });
            }
            else
            {
                Service.Use<IChatService>("ChatClient", chatService =>
                {
                    chatService.SendMessage(new SendMessageRequest(_from, to, message));
                });
            }
        }

        ServiceHost _host;

        public void Stop()
        {
            _host.Close();
        }
        
        [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConfigurationName="ChatServer")]
        public class IncomingHandler : IChatService
        {
            #region IChatService Members
            [OperationBehavior]
            public void SendMessage(SendMessageRequest request)
            {
                Console.WriteLine(request.From + ": " + request.Message);
            }
            #endregion
        }

        [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConfigurationName = "ChatServer2")]
        public class IncomingHandler2 : IChatService2
        {
            #region IChatService Members
            [OperationBehavior]
            public void SendMessage(SendMessageRequest2 request)
            {
                Console.WriteLine(request.From + ": " + request.Title + "\r\n"+request.Message);
            }
            #endregion
        }       

    }

    
    [DataContract]
    public class ChatFilter : MessageFilter
    {
        public ChatFilter()
        {
        }

        public ChatFilter(string to)
        {
            To = to;
        }
        [DataMember]
        public string To;
        
        public override bool Include(string action, object message)
        {
            SendMessageRequest request = message as SendMessageRequest;
            if (request != null)
            {
                return String.Compare(request.To, To, true) == 0;
            }
            return false;
        }
    }



    [DataContract]
    public class ChatFilter2 : MessageFilter
    {
        public ChatFilter2()
        {
        }

        public ChatFilter2(string to)
        {
            To = to;
        }
        [DataMember]
        public string To;

        public override bool Include(string action, object message)
        {            
            SendMessageRequest2 request2 = message as SendMessageRequest2;
            if (request2 != null)
            {
                return String.Compare(request2.To, To, true) == 0;
            }
            return false;
        }
    }
}
