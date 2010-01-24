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
using System.Threading;
using Yaq.Core;
using NUnit.Framework;

namespace Yaq.Tests
{
	/// <summary>
	/// Summary description for UnitTest1
	/// </summary>
	[TestFixture]
	public class AcceptanceTest
	{
		private string c_queueUrl = "net.tcp://localhost:17206/mq";

		[Test]
		public void IntegrationTest()
		{
			var qn = Guid.NewGuid().ToString("N");
			var cf = new ChannelFactory<IMessageQueue>("QueueServiceEndpoint");
			var service = cf.CreateChannel();
			byte[] content = new byte[10];
			new Random().NextBytes(content);
			service.PutMessage(qn, new Message { Content = content }, TimeSpan.MaxValue);
			var msgs = service.PeekMessages(qn, 10);
			Assert.IsNotNull(msgs);
			Assert.AreEqual(1, msgs.Length);
			var msg = msgs.Single();
			Assert.AreEqual(content, msg.Content);
			Assert.IsNull(msg.PopReceipt);

			msgs = service.GetMessages(qn, 10, TimeSpan.FromMinutes(1));
			Assert.IsNotNull(msgs);
			Assert.AreEqual(1, msgs.Length);
			msg = msgs.Single();
//			Assert.AreEqual(content, msg.Content);
			Assert.IsNotNull(msg.PopReceipt);

			Assert.AreEqual(DeleteError.Ok, service.DeleteMessage(qn, msg.Id, msg.PopReceipt));
			Assert.AreEqual(0, service.PeekMessages(qn, 10).Length);

			service.PutMessage(qn, new Message { Content = content }, TimeSpan.MaxValue);
			msg = service.GetMessages(qn, 10, TimeSpan.FromTicks(10)).Single();
			Thread.Sleep(1);
			service.GetMessages(qn, 10, TimeSpan.FromTicks(10));
			Assert.AreEqual(DeleteError.LostOwnership, service.DeleteMessage(qn, msg.Id, msg.PopReceipt));
			Assert.AreEqual(1, service.PeekMessages(qn, 10).Length);
		}

		[Test]
		public void AsyncCallTest()
		{
			using (var host = new ServiceHost(typeof(MessageQueue)))
			{
				var binding = new NetTcpBinding();
				binding.TransactionFlow = true;
				binding.TransactionProtocol = TransactionProtocol.WSAtomicTransactionOctober2004;
				binding.TransferMode = TransferMode.Streamed;

				host.AddServiceEndpoint(
					typeof(IMessageQueue),
					binding,
					c_queueUrl
				);

				host.Open();

				var cf = new ChannelFactory<IAsyncMessageQueue>(binding, c_queueUrl);
				var service = cf.CreateChannel();

				var qn = Guid.NewGuid().ToString("N");
				byte[] content = new byte[10];
				new Random().NextBytes(content);
				service.EndPutMessage(service.BeginPutMessage(qn, new Message { Content = content }, TimeSpan.MaxValue, null, null));
				
				var @event = new ManualResetEvent(false);
				Message[] msgs = null;
				service.BeginGetMessages(qn, 10, TimeSpan.FromMinutes(1), (ar) =>
				{
					msgs = service.EndGetMessages(ar);
					@event.Set();
				}, null);
				Assert.IsTrue(@event.WaitOne(2000));
				Assert.AreEqual(1, msgs.Length);
			}
		}

		[Test]
		public void TaskManagerTest()
		{
			using (var host = new ServiceHost(typeof(MessageQueue)))
			{
				var binding = new NetTcpBinding();
				binding.TransactionFlow = true;
				binding.TransactionProtocol = TransactionProtocol.WSAtomicTransactionOctober2004;
				binding.TransferMode = TransferMode.Streamed;

				host.AddServiceEndpoint(
					typeof(IMessageQueue),
					binding,
					c_queueUrl
				);

				host.Open();

				var cf = new ChannelFactory<IAsyncMessageQueue>(binding, c_queueUrl);

				var queuesrv = cf.CreateChannel();
				string qn = Guid.NewGuid().ToString("N");

				const int mcnt = 3;

				for (int i = 0; i < mcnt; i++)
				{
					queuesrv.PutMessage(qn, new Message(), TimeSpan.MaxValue);
				}

				var latch = new CountdownLatch(mcnt);

				TaskManager tm = new TaskManager(() => cf.CreateChannel());
				tm.Tasks.Add(
					new TaskInfo
					{
						Queue = qn,
						Task = new TestTask(() => latch.Signal()),
						VisibilitySpan = TimeSpan.FromMinutes(1),
						MaxInstances = mcnt,
						PollSpan =
						TimeSpan.FromMilliseconds(500)
					});

				tm.Start();

				Assert.IsTrue(latch.Wait(6000));
			}
		}

		private class TestTask : ITask
		{
			private Action _action;

			public TestTask(Action action)
			{
				_action = action;
			}

			#region ITask Members

			public bool Run(TaskInfo info, Message msg)
			{
				_action();
				return true;
			}

			#endregion
		}

		// http://msdn.microsoft.com/en-us/magazine/cc163427.aspx
		private class CountdownLatch
		{
			private int m_remain;
			private EventWaitHandle m_event;

			public CountdownLatch(int count)
			{
				m_remain = count;
				m_event = new ManualResetEvent(false);
			}

			public void Signal()
			{
				// The last thread to signal also sets the event.
				if (Interlocked.Decrement(ref m_remain) == 0)
					m_event.Set();
			}

			public bool Wait(int ms)
			{
				return m_event.WaitOne(ms);
			}
		}
	}
}
