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
using System.Linq;
using System.ServiceModel;
using System.Data.Linq;

namespace Yaq.Core
{
	[ServiceBehavior(
		ConcurrencyMode = ConcurrencyMode.Multiple,
		InstanceContextMode = InstanceContextMode.Single
	)]
	public class MessageQueue : IMessageQueue
	{
		#region IMessageQueue Members

		public Message[] GetMessages(string queueName, int numberOfMessages, TimeSpan visibilityTimeout)
		{
			Utils.Initialize();

			using (var dc = new Data.MessageDataContext())
			{
				var now = DateTime.UtcNow;

				var qry = dc.ExecuteQuery<Data.Message>(Utils.GetMessageSql,
					numberOfMessages,
					Guid.NewGuid(),
					now + visibilityTimeout,
					now,
					queueName);

				return qry.Select(m => m.ToMessage()).ToArray();
			}
		}

		public Message[] PeekMessages(string queueName, int numberOfMessages)
		{
			Utils.Initialize();

			using (var dc = new Data.MessageDataContext())
			{
				var qry = dc.ExecuteQuery<Data.Message>(Utils.PeekMessageSql,
					numberOfMessages,
					queueName);

				return qry.Select(m => m.ToMessage()).Select(m => { m.PopReceipt = null; return m; }).ToArray();
			}
		}

		public DeleteError DeleteMessage(string queueName, long messageId, string popReceipt)
		{
			using (var dc = new Data.MessageDataContext())
			{
				var msg = dc.Messages.First(m => m.Id == messageId);
				if (msg == null) return DeleteError.NotFound;
				if (msg.Queue != queueName) return DeleteError.NotFound;
				if (msg.PopReceipt.GetValueOrDefault().ToString("N") != popReceipt) return DeleteError.LostOwnership;

				dc.Messages.DeleteOnSubmit(msg);
				dc.SubmitChanges();
			}

			return DeleteError.Ok;
		}

		public long EstimateApproximateCount(string queueName)
		{
			throw new NotImplementedException();
		}

		public void Clear(string queueName)
		{
			throw new NotImplementedException();
		}

		public void PutMessage(string queueName, Message message, TimeSpan timeToLive)
		{
			var msg = new Data.Message();
			msg.Queue = queueName;
			if (message.Content != null)
				msg.Content = message.Content;

			using (var dc = new Data.MessageDataContext())
			{
				dc.Messages.InsertOnSubmit(msg);
				dc.SubmitChanges();
			}
		}

		#endregion
	}
}
