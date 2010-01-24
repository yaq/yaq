/*
 * Yaq.NET - Yet Another Queuing/Processing engine for .NET
 * Copyright (C) 2009 Boris Byk
 * Url: http://github.com/yaq/yaq
 * Email: borison@gmail.com
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 */

using System;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace Yaq.Core
{
	[DataContract(Namespace = Utils.ServiceNamespace)]
	public class Message
	{
		[DataMember]
		public long Id { get; set; }

		[DataMember]
		public DateTime Queued { get; set; }

		[DataMember]
		public DateTime TakenTill { get; set; }

		[DataMember]
		public byte[] Content { get; set; }

		[DataMember]
		public string PopReceipt { get; set; }
	}

	internal static class MessageExtension
	{
		internal static Message ToMessage(this Data.Message dmsg)
		{
			return new Message
			{
				Id = dmsg.Id,
				TakenTill = dmsg.TakenTill.GetValueOrDefault(),
				Queued = dmsg.Queued.GetValueOrDefault(),
				Content = dmsg.Content == null ? null : dmsg.Content.ToArray(),
				PopReceipt = dmsg.PopReceipt.GetValueOrDefault().ToString("N"),
			};
		}
	}

	[DataContract(Namespace = Utils.ServiceNamespace)]
	public enum DeleteError
	{
		[EnumMember]
		Ok,
		[EnumMember]
		NotFound,
		[EnumMember]
		LostOwnership,
	}

	[ServiceContract(
		Name = "queue",
		Namespace = Utils.ServiceNamespace
	)]
	public interface IMessageQueue
	{
		/// <summary>
		/// Ensure to return not null.
		/// </summary>
		/// <param name="queueName"></param>
		/// <param name="numberOfMessages"></param>
		/// <param name="visibilityTimeout"></param>
		/// <returns>An array of type <see cref="Message"/>. It shouldn't return null.</returns>
		[OperationContract]
		Message[] GetMessages(string queueName, int numberOfMessages, TimeSpan visibilityTimeout);

		[OperationContract]
		Message[] PeekMessages(string queueName, int numberOfMessages);

		[OperationContract]
		DeleteError DeleteMessage(string queueName, long messageId, string popReceipt);

		[OperationContract]
		long EstimateApproximateCount(string queueName);

		[OperationContract]
		void Clear(string queueName);

		[OperationContract]
		void PutMessage(string queueName, Message message, TimeSpan timeToLive);
	}

	[ServiceContract(
		Namespace = Utils.ServiceNamespace,
		Name = "queue"
	)]
	public interface IAsyncMessageQueue : IMessageQueue
	{
		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginGetMessages(string queueName, int numberOfMessages, TimeSpan visibilityTimeout, AsyncCallback cb, object state);
		Message[] EndGetMessages(IAsyncResult ar);

		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginPeekMessages(string queueName, int numberOfMessages, AsyncCallback cb, object state);
		Message[] EndPeekMessages(IAsyncResult ar);

		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginDeleteMessage(string queueName, long messageId, string popReceipt, AsyncCallback cb, object state);
		DeleteError EndDeleteMessage(IAsyncResult ar);

		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginEstimateApproximateCount(string queueName, AsyncCallback cb, object state);
		long EndEstimateApproximateCount(IAsyncResult ar);

		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginClear(string queueName, AsyncCallback cb, object state);
		void EndClear(IAsyncResult ar);

		[OperationContract(AsyncPattern = true)]
		IAsyncResult BeginPutMessage(string queueName, Message message, TimeSpan timeToLive, AsyncCallback cb, object state);
		void EndPutMessage(IAsyncResult ar);
	}
}
