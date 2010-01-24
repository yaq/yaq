using System;
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

using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceModel;
using System.Threading;
using log4net;

namespace Yaq.Core
{
	public interface ITask
	{
		bool Run(TaskInfo info, Message msg);
	}

	public class TaskInfo
	{
		public TimeSpan PollSpan { get; set; }
		public TimeSpan VisibilitySpan { get; set; }
		public ITask Task { get; set; }
		public string Queue { get; set; }
		public int MaxInstances { get; set; }

		internal SafeTimer m_timer;
		internal int m_cnt;
		internal int m_isInLoad;
	}

	public class TaskManager
	{
		public Action<Exception> ExProcessor;
		private static readonly ILog _log = LogManager.GetLogger(typeof(TaskManager));
		private Func<IAsyncMessageQueue> _factory;
		private List<TaskInfo> _tasks;

		public TaskManager(Func<IAsyncMessageQueue> factory)
		{
			if (factory == null) throw new ArgumentNullException("factory");
			_factory = factory;
		}

		public List<TaskInfo> Tasks
		{
			get
			{
				if (_tasks == null)
				{
					_tasks = new List<TaskInfo>();
				}

				return _tasks;
			}
		}

		public void Start()
		{
			if (ExProcessor == null) ExProcessor = ex => Trace.WriteLine(ex);
			if (_tasks == null) return;

			foreach (TaskInfo ti in _tasks)
			{
				ti.m_timer = new SafeTimer(OnNextPoll, ti, ti.PollSpan, ti.PollSpan);
			}
		}

		public void Stop()
		{
			if (_tasks == null) return;

			foreach (TaskInfo ti in _tasks)
			{
				ti.m_timer.Dispose();
			}
		}

		private void OnNextPoll(object state)
		{
			TaskInfo ti = (TaskInfo)state;

			if (Interlocked.CompareExchange(ref ti.m_isInLoad, 1, 0) == 0)
			{
				int cnt = ti.MaxInstances - Thread.VolatileRead(ref ti.m_cnt);
				Interlocked.Add(ref ti.m_cnt, cnt);

				if (cnt > 0)
				{
					LoadAndExec(ti, cnt);
				}
				else
				{
					Interlocked.Exchange(ref ti.m_isInLoad, 0);
				}
			}
		}

		private void LoadAndExec(TaskInfo ti, int cnt)
		{
			IAsyncMessageQueue service = null;
			ICommunicationObject co = null;

			try
			{
				service = _factory();
				co = service as ICommunicationObject;

				service.BeginGetMessages(ti.Queue, cnt, ti.VisibilitySpan, (ar) =>
				{
					Message[] msgs = null;
					try
					{
						msgs = service.EndGetMessages(ar);
						if (co != null) co.Close();
					}
					catch (Exception ex)
					{
						ExProcessor(ex);
						if (co != null) co.Abort();
					}
					finally
					{
						int dec = msgs == null ? -cnt : msgs.Length - cnt;
						if (dec < 0)
						{
							Interlocked.Add(ref ti.m_cnt, dec);
						}
						Interlocked.Exchange(ref ti.m_isInLoad, 0);
					}

					if (msgs == null) return;

					foreach (var imsg in msgs)
					{
						ThreadPool.QueueUserWorkItem((state) =>
						{
							var msg = (Message)state;
							bool delete;
							try
							{
								delete = ti.Task.Run(ti, msg);
							}
							catch (Exception ex)
							{
								ExProcessor(ex);
								delete = true;
							}

							Interlocked.Decrement(ref ti.m_cnt);

							if (delete)
							{
								IAsyncMessageQueue srv = null;
								ICommunicationObject sco = null;

								try
								{
									srv = _factory();
									sco = service as ICommunicationObject;

									srv.BeginDeleteMessage(ti.Queue, msg.Id, msg.PopReceipt, (ar2) =>
									{
										try
										{
											srv.EndDeleteMessage(ar2);
											if (sco != null) sco.Close();
										}
										catch (Exception ex)
										{
											ExProcessor(ex);
											if (sco != null) sco.Abort();
										}
									},
									null);
								}
								catch (Exception ex)
								{
									ExProcessor(ex);
									if (sco != null) sco.Abort();
								}
							}
						}, imsg); // pass current msg through the state. we cannot use closures here.
					}
				}
				, null);
			}
			catch (Exception ex)
			{
				ExProcessor(ex);
				Interlocked.Add(ref ti.m_cnt, -cnt);
				Interlocked.Exchange(ref ti.m_isInLoad, 0);
				if (co != null) co.Abort();
			}
		}
	}
}
