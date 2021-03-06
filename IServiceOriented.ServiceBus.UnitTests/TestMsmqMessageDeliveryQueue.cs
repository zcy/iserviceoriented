﻿using System;
using System.Transactions;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using System.Runtime.Serialization;
using IServiceOriented.ServiceBus.Collections;

using System.ServiceModel;
using IServiceOriented.ServiceBus.Delivery;
using IServiceOriented.ServiceBus.Delivery.Formatters;
using IServiceOriented.ServiceBus.Dispatchers;
using System.ServiceModel.Channels;

namespace IServiceOriented.ServiceBus.UnitTests
{
    [Serializable]
    [DataContract]
    public class ComplexData
    {
        public ComplexData()
        {
        }
        public ComplexData(int value1, int value2)
        {
            Value1 = value1;
            Value2 = value2;
        }

        [DataMember]
        public int Value1;
        [DataMember]
        public int Value2;

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            if (obj.GetType() != typeof(ComplexData)) return false;

            ComplexData objData = (ComplexData)obj;

            return Value1 == objData.Value1 && Value2 == objData.Value2;
        }

        public override int GetHashCode()
        {
            return Value1.GetHashCode();
        }

        public static bool operator ==(ComplexData v1, ComplexData v2)
        {
            return v1.Equals(v2);
        }

        public static bool operator !=(ComplexData v1, ComplexData v2)
        {
            return !v1.Equals(v2);
        }
    }

    [ServiceContract]
    public interface ISendComplexData
    {
        [OperationContract(Action="send")]
        void Send(ComplexData data);
    }


    [ServiceContract]
    public interface ISendMessageContract
    {
        [OperationContract(Action="send")]
        void Send(MessageContractMessage message);
    }

    [MessageContract]
    public class MessageContractMessage
    {
        [MessageBodyMember]
        public string Data;
    }

    [DataContract]
    public class SendFault
    {
        [DataMember]
        public string Data;
    }

    [ServiceContract]
    public interface ISendDataContract
    {
        [FaultContract(typeof(SendFault), Action="sendFault")]
        [OperationContract(Action="send")]
        void Send(DataContractMessage message);
    }

    [DataContract]
    public class DataContractMessage
    {
        [DataMember]
        public string Data;
    }

    [TestFixture]
    public class TestMsmqMessageDeliveryQueue
    {
        public TestMsmqMessageDeliveryQueue()
        {
            
        }

        
        [TestFixtureSetUp]
        public void Initialize()
        {         
            if (Config.TestQueuePath == null || Config.RetryQueuePath == null || Config.FailQueuePath == null)
            {
                Assert.Ignore("Test msmq queues not configured, skipping msmq tests");
            }
            else
            {
                recreateQueue();
            }
            
        }
        void recreateQueue()
        {
            // Delete test queues if they already exist

            if (MsmqMessageDeliveryQueue.Exists(Config.TestQueuePath))
            {
                MsmqMessageDeliveryQueue.Delete(Config.TestQueuePath);
            }            
            // Create test queue
            MsmqMessageDeliveryQueue.Create(Config.TestQueuePath);
        }

        [Test]
        public void Enqueue_Transactions_Abort_Properly()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(IContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(IContract)));            

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);

            SubscriptionEndpoint endpoint = new SubscriptionEndpoint(Guid.NewGuid(), "SubscriptionName", "http://localhost/test", "SubscriptionConfigName", typeof(IContract), new WcfProxyDispatcher(), new PassThroughMessageFilter());

            MessageDelivery enqueued = new MessageDelivery(endpoint.Id, typeof(IContract), "PublishThis", "randomMessageData", 3, new MessageDeliveryContext());
            Assert.IsNull(queue.Peek(TimeSpan.FromSeconds(1))); // make sure queue is null before starting

            // Enqueue, but abort transaction
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(enqueued);
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(5));
                Assert.IsNull(dequeued);
            }
        }


        [Test]
        public void Enqueue_Transactions_Commit_Properly()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(IContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(IContract)));            


            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            SubscriptionEndpoint endpoint = new SubscriptionEndpoint(Guid.NewGuid(), "SubscriptionName", "http://localhost/test", "SubscriptionConfigName", typeof(IContract), new WcfProxyDispatcher(), new PassThroughMessageFilter());

            MessageDelivery enqueued = new MessageDelivery(endpoint.Id, typeof(IContract), "PublishThis", "randomMessageData", 3, new MessageDeliveryContext());
            Assert.IsNull(queue.Peek(TimeSpan.FromSeconds(1))); // Make sure queue is null

            // Enqueue and commit transaction
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(enqueued);
                ts.Complete();
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
                ts.Complete();
            }            
        }

        [Test]
        public void Dequeue_Transactions_Abort_Properly()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(IContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(IContract)));            

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            SubscriptionEndpoint endpoint = new SubscriptionEndpoint(Guid.NewGuid(), "SubscriptionName", "http://localhost/test", "SubscriptionConfigName", typeof(IContract), new WcfProxyDispatcher(), new PassThroughMessageFilter());

            MessageDelivery enqueued = new MessageDelivery(endpoint.Id, typeof(IContract), "PublishThis", "randomMessageData", 3, new MessageDeliveryContext());
            Assert.IsNull(queue.Peek(TimeSpan.FromSeconds(1))); // Make sure queue is null

            // Enqueue and commit transaction
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(enqueued);
                ts.Complete();
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(5));
                Assert.IsNotNull(dequeued);
            }
        }

        [Test]
        public void Dequeue_Transactions_Commit_Properly()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(IContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(IContract)));            

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            SubscriptionEndpoint endpoint = new SubscriptionEndpoint(Guid.NewGuid(), "SubscriptionName", "http://localhost/test", "SubscriptionConfigName", typeof(IContract), new WcfProxyDispatcher(), new PassThroughMessageFilter());

            MessageDelivery enqueued = new MessageDelivery(endpoint.Id, typeof(IContract), "PublishThis", "randomMessageData", 3, new MessageDeliveryContext());
            Assert.IsNull(queue.Peek(TimeSpan.FromSeconds(1))); // Make sure queue is null

            // Enqueue and commit transaction
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(enqueued);
                ts.Complete();
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(10));
                Assert.IsNotNull(dequeued);
                Assert.AreEqual(dequeued.MessageDeliveryId, enqueued.MessageDeliveryId);
                ts.Complete();
            }

            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery dequeued = queue.Dequeue(TimeSpan.FromSeconds(5));
                Assert.IsNull(dequeued);
            }
        }

        [Test]
        public void MessageContractFormatter_Can_Roundtrip_DataContract()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(ISendDataContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(ISendDataContract)));            

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            string action = "send";
            DataContractMessage outgoing = new DataContractMessage() { Data = "This is a test" };

            Dictionary<MessageDeliveryContextKey, object> context = new Dictionary<MessageDeliveryContextKey, object>();
            context.Add(new MessageDeliveryContextKey("test"), "value");

            MessageDelivery outgoingDelivery = new MessageDelivery(Guid.NewGuid(), typeof(ISendDataContract), action, outgoing, 5, new MessageDeliveryContext(context));
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(outgoingDelivery);
                ts.Complete();
            }
            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery delivery = queue.Dequeue(TimeSpan.FromMinutes(1));
                Assert.AreEqual(typeof(DataContractMessage), delivery.Message.GetType());
                DataContractMessage incoming = (DataContractMessage)delivery.Message;
                Assert.AreEqual(incoming.Data, outgoing.Data);
                Assert.AreEqual(context[new MessageDeliveryContextKey("test")], delivery.Context[new MessageDeliveryContextKey("test")]);

                Assert.AreEqual(outgoingDelivery.Action, delivery.Action);
                Assert.AreEqual(outgoingDelivery.ContractType, delivery.ContractType);
                Assert.AreEqual(outgoingDelivery.MaxRetries, delivery.MaxRetries);
                Assert.AreEqual(outgoingDelivery.MessageDeliveryId, delivery.MessageDeliveryId);
                Assert.AreEqual(outgoingDelivery.RetryCount, delivery.RetryCount);
                Assert.AreEqual(outgoingDelivery.TimeToProcess, delivery.TimeToProcess);
                Assert.AreEqual(outgoingDelivery.SubscriptionEndpointId, delivery.SubscriptionEndpointId);
                ts.Complete();
            }
        }

        [Test]
        public void MessageContractFormatter_Can_Roundtrip_FaultContract()
        {
            recreateQueue();


            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(ISendDataContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(ISendDataContract)));

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            string action = "sendFault";
            FaultException<SendFault> outgoing = new FaultException<SendFault>(new SendFault() { Data = "This is a test" });

            Dictionary<MessageDeliveryContextKey, object> context = new Dictionary<MessageDeliveryContextKey, object>();
            context.Add(new MessageDeliveryContextKey("test"), "value");

            MessageDelivery outgoingDelivery = new MessageDelivery(Guid.NewGuid(), typeof(ISendDataContract), action, outgoing, 5, new MessageDeliveryContext(context));
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(outgoingDelivery);
                ts.Complete();
            }
            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery delivery = queue.Dequeue(TimeSpan.FromMinutes(1));
                Assert.AreEqual(typeof(FaultException<SendFault>), delivery.Message.GetType());
                FaultException<SendFault> incoming = (FaultException<SendFault>)delivery.Message;
                Assert.AreEqual(incoming.Data, outgoing.Data);
                Assert.AreEqual(context[new MessageDeliveryContextKey("test")], delivery.Context[new MessageDeliveryContextKey("test")]);

                Assert.AreEqual(outgoingDelivery.Action, delivery.Action);
                Assert.AreEqual(outgoingDelivery.ContractType, delivery.ContractType);
                Assert.AreEqual(outgoingDelivery.MaxRetries, delivery.MaxRetries);
                Assert.AreEqual(outgoingDelivery.MessageDeliveryId, delivery.MessageDeliveryId);
                Assert.AreEqual(outgoingDelivery.RetryCount, delivery.RetryCount);
                Assert.AreEqual(outgoingDelivery.TimeToProcess, delivery.TimeToProcess);
                Assert.AreEqual(outgoingDelivery.SubscriptionEndpointId, delivery.SubscriptionEndpointId);
                ts.Complete();
            }
        }



        [Test]
        public void MessageContractFormatter_Can_Roundtrip_MessageContract()
        {

            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;

            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(ISendMessageContract)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(ISendMessageContract)));            

            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);
            string action = "send";
            MessageContractMessage outgoing = new MessageContractMessage() { Data = "This is a test" };

            Dictionary<MessageDeliveryContextKey, object> context = new Dictionary<MessageDeliveryContextKey, object>();
            context.Add(new MessageDeliveryContextKey("test"), "value");

            MessageDelivery outgoingDelivery = new MessageDelivery(Guid.NewGuid(), typeof(ISendMessageContract), action, outgoing, 5, new MessageDeliveryContext(context));
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(outgoingDelivery);
                ts.Complete();
            }
            using (TransactionScope ts = new TransactionScope())
            {
                MessageDelivery delivery = queue.Dequeue(TimeSpan.FromMinutes(1));
                Assert.AreEqual(typeof(MessageContractMessage), delivery.Message.GetType());
                MessageContractMessage incoming = (MessageContractMessage)delivery.Message;
                Assert.AreEqual(incoming.Data, outgoing.Data);
                Assert.AreEqual(context[new MessageDeliveryContextKey("test")], delivery.Context[new MessageDeliveryContextKey("test")]);

                Assert.AreEqual(outgoingDelivery.Action, delivery.Action);
                Assert.AreEqual(outgoingDelivery.ContractType, delivery.ContractType);
                Assert.AreEqual(outgoingDelivery.MaxRetries, delivery.MaxRetries);
                Assert.AreEqual(outgoingDelivery.MessageDeliveryId, delivery.MessageDeliveryId);
                Assert.AreEqual(outgoingDelivery.RetryCount, delivery.RetryCount);
                Assert.AreEqual(outgoingDelivery.TimeToProcess, delivery.TimeToProcess);
                Assert.AreEqual(outgoingDelivery.SubscriptionEndpointId, delivery.SubscriptionEndpointId);
                ts.Complete();
            }
        }
        [Test]
        public void Can_Deliver_Complex_Message()
        {
            testMessageDelivery<ISendComplexData>("send", new ComplexData(91302,1120));
        }

        
        [Test]
        public void Can_Deliver_Simple_Message()
        {
            testMessageDelivery<IContract>("PublishThis", "testMessageData");
        }   
        
        void testMessageDelivery<T>(string messageAction, object messageData)
        {
            Type interfaceType = typeof(T);

            BinaryMessageEncodingBindingElement element = new BinaryMessageEncodingBindingElement();
            MessageEncoder encoder = element.CreateMessageEncoderFactory().Encoder;
            MessageDeliveryFormatter formatter = new MessageDeliveryFormatter(new ConverterMessageDeliveryReaderFactory(encoder, typeof(T)), new ConverterMessageDeliveryWriterFactory(encoder, typeof(T)));            


            MsmqMessageDeliveryQueue queue = new MsmqMessageDeliveryQueue(Config.TestQueuePath, formatter);

            SubscriptionEndpoint endpoint = new SubscriptionEndpoint(Guid.NewGuid(), "SubscriptionName", "http://localhost/test", "SubscriptionConfigName", interfaceType, new WcfProxyDispatcher(), new PassThroughMessageFilter());

            MessageDelivery enqueued = new MessageDelivery(endpoint.Id, interfaceType, messageAction, messageData, 3, new MessageDeliveryContext());
                
            using (TransactionScope ts = new TransactionScope())
            {
                queue.Enqueue(enqueued);
                ts.Complete();
            }

            // Peek
            MessageDelivery dequeued = queue.Peek(TimeSpan.FromSeconds(30));
            Assert.IsNotNull(dequeued);
            Assert.AreEqual(enqueued.Action, dequeued.Action);
            Assert.AreEqual(enqueued.SubscriptionEndpointId, dequeued.SubscriptionEndpointId);
           
            using (TransactionScope ts = new TransactionScope())
            {   
                // Pull for real
                dequeued = queue.Dequeue(TimeSpan.FromSeconds(30));
                ts.Complete();
            }
            Assert.IsNotNull(dequeued); 
            Assert.AreEqual(enqueued.Action, dequeued.Action);
            Assert.AreEqual(enqueued.SubscriptionEndpointId, dequeued.SubscriptionEndpointId);
            
            // Should now be empty
            dequeued = queue.Peek(TimeSpan.FromSeconds(1));            
            Assert.IsNull(dequeued);
        }
    }
}
