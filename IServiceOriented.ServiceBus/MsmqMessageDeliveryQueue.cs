﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization;

using System.Messaging;

namespace IServiceOriented.ServiceBus
{
    public class MessageDeliveryDataContractFormatter : IMessageFormatter
    {
        #region IMessageFormatter Members

        public bool CanRead(Message message)
        {
            return message.Body is MessageDelivery;
        }

        DataContractSerializer _serializer;
        DataContractSerializer getSerializer(bool refresh)
        {
            if (_serializer == null || refresh) // doesn't have to be thread safe. ok to overwrite on race.
            {
                _serializer = new DataContractSerializer(typeof(MessageDelivery), MessageDelivery.GetKnownTypes());
            }
            return _serializer;
        }

        public void RefreshKnownTypes()
        {
            getSerializer(true);
        }

        public object Read(Message message)
        {
            DataContractSerializer serializer = getSerializer(false);
            return (MessageDelivery)serializer.ReadObject(message.BodyStream);
        }

        public void Write(Message message, object obj)
        {            
            DataContractSerializer serializer = getSerializer(false);
            MemoryStream ms = new MemoryStream();
            serializer.WriteObject(ms, obj);
            ms.Position = 0;
            message.BodyStream = ms;
        }

        #endregion

        #region ICloneable Members

        public object Clone()
        {
            return new MessageDeliveryDataContractFormatter();
        }

        #endregion
    }
    public class MsmqMessageDeliveryQueue : IMessageDeliveryQueue, IDisposable
    {
        public MsmqMessageDeliveryQueue(string path)
        {
            _queue = new MessageQueue(path);
            _formatter = new MessageDeliveryDataContractFormatter();
        }

        public MsmqMessageDeliveryQueue(string path, MessageQueueTransactionType transactionType)
        {
            _queue = new MessageQueue(path);
            _formatter = new MessageDeliveryDataContractFormatter();
            _transactionType = transactionType;
        }


        public MsmqMessageDeliveryQueue(string path, bool createIfNotExists)
        {
            if (createIfNotExists)
            {
                if (!MessageQueue.Exists(path))
                {
                    MessageQueue.Create(path, true);
                }
            }
            _queue = new MessageQueue(path);
            _formatter = new MessageDeliveryDataContractFormatter();
        }

        
        public MsmqMessageDeliveryQueue(MessageQueue queue)
        {
            _queue = queue;
            _formatter = new MessageDeliveryDataContractFormatter();
        }

        public static void Create(string path)
        {
            MessageQueue.Create(path, true);
        }

        public static void Delete(string path)
        {
            MessageQueue.Delete(path);
        }

        MessageQueueTransactionType _transactionType = MessageQueueTransactionType.Automatic;
        public MessageQueueTransactionType TransactionType
        {
            get
            {
                return _transactionType;
            }
            set
            {
                _transactionType = value;
            }
        }
        MessageQueue _queue;
        IMessageFormatter _formatter;

        public IMessageFormatter Formatter
        {
            get
            {
                return _formatter;
            }
            set
            {
                _formatter = value;
            }
        }


        private System.Messaging.Message createMessage(MessageDelivery value)
        {
            System.Messaging.Message message = new System.Messaging.Message();
            message.Label = value.MessageId;
            message.Formatter = _formatter;
            message.Body = value;
            return message;
        }

        private MessageDelivery createObject(System.Messaging.Message message)
        {
            message.Formatter = _formatter;
            try
            {
                return (MessageDelivery)message.Body;
            }
            catch (Exception ex)
            {
                throw new PoisonMessageException("The message body could not be deserialized", ex);
            }
        }

        public void Enqueue(MessageDelivery value)
        {
            if (_disposed) throw new ObjectDisposedException("MsmqMessageDeliveryQueue");
            
            System.Messaging.Message message = createMessage(value);
            value.IncrementQueueCount();
            _queue.Send(message, _transactionType);
        }

        public MessageDelivery Peek(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException("MsmqMessageDeliveryQueue");
            
            try
            {
                System.Messaging.Message message = _queue.Peek(timeout);
                return createObject(message);
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    return default(MessageDelivery);
                }
                throw;
            }
        }

        public MessageDelivery Dequeue(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException("MsmqMessageDeliveryQueue");
            
            try
            {
                System.Messaging.Message message = _queue.Receive(timeout, _transactionType);
                return createObject(message);
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    return default(MessageDelivery);
                }
                throw;
            }
        }

        public MessageDelivery Dequeue(string id, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException("MsmqMessageDeliveryQueue");

            try
            {
                using(System.Messaging.MessageEnumerator msgEnum = _queue.GetMessageEnumerator2())
                {
                    if(msgEnum.Current.Label == id)
                    {
                        Message message = _queue.ReceiveById(msgEnum.Current.Id, _transactionType);
                        return createObject(message);        
                    }
                }

                return null;
            }
            catch (MessageQueueException ex)
            {
                if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    throw new TimeoutException("The timeout was reached before the specified message could be returned.");
                }
                else if (ex.MessageQueueErrorCode == MessageQueueErrorCode.MessageNotFound)
                {
                    return default(MessageDelivery);
                }
                throw;
            }
        }

        public IEnumerable<MessageDelivery> ListMessages()
        {
            if (_disposed) throw new ObjectDisposedException("MsmqMessageDeliveryQueue");

            MessageEnumerator enumerator = _queue.GetMessageEnumerator2();
            while (enumerator.MoveNext())
            {
                yield return createObject(enumerator.Current);
            }
        }

        volatile bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                if(_queue != null) _queue.Dispose();
                _queue = null;

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }
    }
	
	

}