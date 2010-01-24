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
using System.Data.Linq.Mapping;
using System.Linq.Expressions;
using log4net;
using System.Threading;

namespace Yaq.Core
{
	internal static class Utils
	{
		public const string ServiceNamespace = "yaq:services";

		private static object _sync = new Object();
		private static bool _initialized;
		internal static string PeekMessageSql;
		internal static string GetMessageSql;

		internal static void Initialize()
		{
			if (_initialized) return;

			lock (_sync)
			{
				if (_initialized) return;

				var dc = new Data.MessageDataContext();
				var mt = dc.Mapping.GetMetaType(typeof(Data.Message));

				var queue = mt.GetMetaDataMember(m => m.Queue);
				var takenTill = mt.GetMetaDataMember(m => m.TakenTill);
				var popReceipt = mt.GetMetaDataMember(m => m.PopReceipt);
				var id = mt.GetMetaDataMember(m => m.Id);

				PeekMessageSql = String.Format("select top({{0}}) * from {0} where [{1}] = {{1}} order by [{2}]",
					mt.Table.TableName,
					queue.MappedName,
					id.MappedName);

				GetMessageSql = String.Format(@"
					update top({{0}}) {0}
					set [{1}] = {{1}}, [{2}] = {{2}}
						output inserted.*
					from {0} with(readpast, index([Message.IX.ByQueue]))
					where 
						([{2}] is null or [{2}] <= {{3}})
						and [{3}] = {{4}}",
						mt.Table.TableName,
						popReceipt.MappedName,
						takenTill.MappedName,
						queue.MappedName);

				_initialized = true;
			}
		}

		internal static MetaDataMember GetMetaDataMember(this MetaType mt, Expression<Func<Data.Message, object>> lambda)
		{
			if (mt == null)
				throw new ArgumentNullException("mt");

			if (lambda == null)
				throw new ArgumentNullException("lambda");

			var body = lambda.Body;
			if ((body != null) && ((body.NodeType == ExpressionType.Convert) || (body.NodeType == ExpressionType.ConvertChecked)))
			{
				body = ((UnaryExpression)body).Operand;
			}

			var expr = body as MemberExpression;
			if ((expr == null) || (expr.Expression.NodeType != ExpressionType.Parameter))
			{
				throw new InvalidOperationException("Invalid member specification.");
			}

			return mt.GetDataMember(expr.Member);
		}
	}

	internal sealed class SafeTimer
	{
		private static readonly ILog _log = LogManager.GetLogger(typeof(SafeTimer));

		private Timer _timer;
		private int _rqInCallback;

		public SafeTimer(
			TimerCallback callback,
			Object state,
			TimeSpan dueTime,
			TimeSpan period)
			: this(
			callback,
			state,
			(uint)dueTime.TotalMilliseconds,
			(uint)period.TotalMilliseconds,
			"error {0}",
			"overlap {0} ms period.")
		{
		}

		public SafeTimer(
			TimerCallback callback,
			Object state,
			TimeSpan dueTime,
			TimeSpan period,
			string onExceptionMessage,
			string onOverlapMessage)
			: this(
			callback,
			state,
			(uint)dueTime.TotalMilliseconds,
			(uint)period.TotalMilliseconds,
			onExceptionMessage,
			onOverlapMessage)
		{
		}

		public SafeTimer(
			TimerCallback callback,
			Object state,
			int dueTime,
			int period,
			string onExceptionMessage,
			string onOverlapMessage)
			: this(
			callback,
			state,
			(uint)dueTime,
			(uint)period,
			onExceptionMessage,
			onOverlapMessage)
		{
		}


		public SafeTimer(
			TimerCallback callback,
			Object state,
			long dueTime,
			long period,
			string onExceptionMessage,
			string onOverlapMessage)
			: this(
			callback,
			state,
			(uint)dueTime,
			(uint)period,
			onExceptionMessage,
			onOverlapMessage)
		{
		}

		public SafeTimer(
			TimerCallback callback,
			Object state,
			uint dueTime,
			uint period,
			string onExceptionMessage,
			string onOverlapMessage)
		{
			_timer = new Timer(
				delegate(object context)
				{
					// Provides reenterant access to cleanup method from the timer.
					// This delegate can be executed simultaneously on two thread
					// pool threads if the timer interval is less than the time
					// required to execute the method.
					//
					// http://msdn2.microsoft.com/en-us/library/3yb9at7c.aspx

					if (Interlocked.CompareExchange(ref _rqInCallback, 1, 0) == 0)
					{
						try
						{
							callback(context);
						}
						catch (Exception ex)
						{
							if (_log.IsFatalEnabled)
							{
								_log.Fatal(onExceptionMessage, ex);
							}
						}
						finally
						{
							Interlocked.Exchange(ref _rqInCallback, 0);
						}
					}
					else
					{
						if (_log.IsWarnEnabled && onOverlapMessage != null)
						{
							_log.WarnFormat(onOverlapMessage, period);
						}
					}
				},
				state,
				dueTime,
				period
			);
		}

		public void Dispose()
		{
			_timer.Dispose();
		}

		public bool Dispose(WaitHandle notifyObject)
		{
			return _timer.Dispose(notifyObject);
		}

		public bool Change(int dueTime, int period)
		{
			return _timer.Change(dueTime, period);
		}

		public bool Change(long dueTime, long period)
		{
			return _timer.Change(dueTime, period);
		}

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			return _timer.Change(dueTime, period);
		}

		public bool Change(uint dueTime, uint period)
		{
			return _timer.Change(dueTime, period);
		}

		public bool IsInCallback
		{
			get { return Thread.VolatileRead(ref _rqInCallback) == 1; }
		}
	}
}
