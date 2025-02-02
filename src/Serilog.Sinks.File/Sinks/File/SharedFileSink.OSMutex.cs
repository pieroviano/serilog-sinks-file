﻿// Copyright 2013-2019 Serilog Contributors
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

#if OS_MUTEX

using System;
using System.IO;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using System.Threading;
using Serilog.Debugging;

namespace Serilog.Sinks.File
{
    /// <summary>
    /// Write log events to a disk file.
    /// </summary>
    [Obsolete("This type will be removed from the public API in a future version; use `WriteTo.File(shared: true)` instead.")]
    public sealed class SharedFileSink : IFileSink, IDisposable
    {
        readonly TextWriter _output;
        readonly FileStream _underlyingStream;
        readonly ITextFormatter _textFormatter;
        readonly long? _fileSizeLimitBytes;
        readonly object _syncRoot = new object();

        const string MutexNameSuffix = ".serilog";
        const int MutexWaitTimeout = 10000;
        readonly Mutex _mutex;

        /// <summary>Construct a <see cref="FileSink"/>.</summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="textFormatter">Formatter used to convert log events to text.</param>
        /// <param name="fileSizeLimitBytes">The approximate maximum size, in bytes, to which a log file will be allowed to grow.
        /// For unrestricted growth, pass null. The default is 1 GB. To avoid writing partial events, the last event within the limit
        /// will be written in full even if it exceeds the limit.</param>
        /// <param name="encoding">Character encoding used to write the text file. The default is UTF-8 without BOM.</param>
        /// <returns>Configuration object allowing method chaining.</returns>
        /// <remarks>The file will be written using the UTF-8 character set.</remarks>
        /// <exception cref="ArgumentNullException">When <paramref name="path"/> is <code>null</code></exception>
        /// <exception cref="ArgumentNullException">When <paramref name="textFormatter"/> is <code>null</code></exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="PathTooLongException">When <paramref name="path"/> is too long</exception>
        /// <exception cref="UnauthorizedAccessException">The caller does not have the required permission to access the <paramref name="path"/></exception>
        /// <exception cref="ArgumentException">Invalid <paramref name="path"/></exception>
        public SharedFileSink(string path, ITextFormatter textFormatter, long? fileSizeLimitBytes, Encoding? encoding = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (fileSizeLimitBytes.HasValue && fileSizeLimitBytes < 1)
                throw new ArgumentException("Invalid value provided; file size limit must be at least 1 byte, or null.");
            _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
            _fileSizeLimitBytes = fileSizeLimitBytes;

            var directory = Path.GetDirectoryName(path);
#if NET35
            if (!LogEventProperty.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
#else
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
#endif
            {
                Directory.CreateDirectory(directory);
            }

            var mutexName = Path.GetFullPath(path).Replace(Path.DirectorySeparatorChar, ':') + MutexNameSuffix;
            _mutex = new Mutex(false, mutexName);
            _underlyingStream = System.IO.File.Open(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _output = new StreamWriter(_underlyingStream, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        bool IFileSink.EmitOrOverflow(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            lock (_syncRoot)
            {
                if (!TryAcquireMutex())
                    return true; // We didn't overflow, but, roll-on-size should not be attempted

                try
                {
                    _underlyingStream.Seek(0, SeekOrigin.End);
                    if (_fileSizeLimitBytes != null)
                    {
                        if (_underlyingStream.Length >= _fileSizeLimitBytes.Value)
                            return false;
                    }

                    _textFormatter.Format(logEvent, _output);
                    _output.Flush();
                    _underlyingStream.Flush();
                    return true;
                }
                finally
                {
                    ReleaseMutex();
                }
            }
        }

        /// <summary>
        /// Emit the provided log event to the sink.
        /// </summary>
        /// <param name="logEvent">The log event to write.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="logEvent"/> is <code>null</code></exception>
        public void Emit(LogEvent logEvent)
        {
            ((IFileSink)this).EmitOrOverflow(logEvent);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_syncRoot)
            {
                _output.Dispose();
#if !NET35
                _mutex.Dispose();
#else
                _mutex.Close();
#endif
            }
        }

        /// <inheritdoc />
        public void FlushToDisk()
        {
            lock (_syncRoot)
            {
                if (!TryAcquireMutex())
                    return;

                try
                {
#if NET35
                    _underlyingStream.Flush();
#else
                    _underlyingStream.Flush(true);
#endif
                }
                finally
                {
                    ReleaseMutex();
                }
            }
        }

        bool TryAcquireMutex()
        {
            try
            {
                if (!_mutex.WaitOne(MutexWaitTimeout))
                {
                    SelfLog.WriteLine("Shared file mutex could not be acquired within {0} ms", MutexWaitTimeout);
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                SelfLog.WriteLine("Inherited shared file mutex after abandonment by another process");
            }

            return true;
        }

        void ReleaseMutex()
        {
            _mutex.ReleaseMutex();
        }
    }
}

#endif
