﻿// Copyright 2016-2019 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.File
{
    /// <summary>
    /// A sink wrapper that periodically flushes the wrapped sink to disk.
    /// </summary>
    [Obsolete("This type will be removed from the public API in a future version; use `WriteTo.File(flushToDiskInterval:)` instead.")]
    public class PeriodicFlushToDiskSink : ILogEventSink, IDisposable
    {
        readonly ILogEventSink _sink;
        readonly Timer _timer;
        int _flushRequired;

        /// <summary>
        /// Construct a <see cref="PeriodicFlushToDiskSink"/> that wraps
        /// <paramref name="sink"/> and flushes it at the specified <paramref name="flushInterval"/>.
        /// </summary>
        /// <param name="sink">The sink to wrap.</param>
        /// <param name="flushInterval">The interval at which to flush the underlying sink.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="sink"/> is <code>null</code></exception>
        public PeriodicFlushToDiskSink(ILogEventSink sink, TimeSpan flushInterval)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));

            if (sink is IFlushableFileSink flushable)
            {
                _timer = new Timer(_ => FlushToDisk(flushable), null, flushInterval, flushInterval);
            }
            else
            {
                _timer = new Timer(_ => { }, null,
#if !NET40 && !NET35
                    Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
#else
                    new TimeSpan(0, 0, 0, 0, -1), new TimeSpan(0, 0, 0, 0, -1));
#endif
                SelfLog.WriteLine("{0} configured to flush {1}, but {2} not implemented", typeof(PeriodicFlushToDiskSink), sink, nameof(IFlushableFileSink));
            }
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            _sink.Emit(logEvent);
            Interlocked.Exchange(ref _flushRequired, 1);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _timer.Dispose();
            (_sink as IDisposable)?.Dispose();
        }

        void FlushToDisk(IFlushableFileSink flushable)
        {
            try
            {
                if (Interlocked.CompareExchange(ref _flushRequired, 0, 1) == 1)
                {
                    // May throw ObjectDisposedException, since we're not trying to synchronize
                    // anything here in the wrapper.
                    flushable.FlushToDisk();
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("{0} could not flush the underlying sink to disk: {1}", typeof(PeriodicFlushToDiskSink), ex);
            }
        }
    }
}
