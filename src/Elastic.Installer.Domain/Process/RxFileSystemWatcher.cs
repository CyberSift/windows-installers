using System;
using System.IO;
using System.Reactive.Linq;

namespace Elastic.Installer.Domain.Process
{
	/// <summary>
	///     This is a wrapper around a file system watcher to use the Rx framework instead of event handlers to handle
	///     notifications of file system changes.
	/// </summary>
	/// <remarks>
	/// Include individual file as nuget package is compiled against unlisted Rx packages
	/// 
	/// https://github.com/g0t4/Rx-FileSystemWatcher
	/// 
	/// The MIT License (MIT)
	/// 
	/// Copyright(c) 2014 g0t4
	/// 
	/// Permission is hereby granted, free of charge, to any person obtaining a copy of
	/// this software and associated documentation files (the "Software"), to deal in
	/// the Software without restriction, including without limitation the rights to
	/// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
	/// the Software, and to permit persons to whom the Software is furnished to do so,
	/// subject to the following conditions:
	/// 
	/// The above copyright notice and this permission notice shall be included in all
	/// copies or substantial portions of the Software.
	/// 
	/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	/// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
	/// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
	/// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
	/// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
	/// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
	/// </remarks>
	public class RxFileSystemWatcher : IDisposable
	{
		public readonly FileSystemWatcher Watcher;

		public IObservable<FileSystemEventArgs> Changed { get; private set; }
		public IObservable<RenamedEventArgs> Renamed { get; private set; }
		public IObservable<FileSystemEventArgs> Deleted { get; private set; }
		public IObservable<ErrorEventArgs> Errors { get; private set; }
		public IObservable<FileSystemEventArgs> Created { get; private set; }

		/// <summary>
		///     Pass an existing FileSystemWatcher instance, this is just for the case where it's not possible to only pass the
		///     configuration, be aware that disposing this wrapper will dispose the FileSystemWatcher instance too.
		/// </summary>
		/// <param name="watcher"></param>
		public RxFileSystemWatcher(FileSystemWatcher watcher)
		{
			Watcher = watcher;

			Changed = Observable
				.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(h => Watcher.Changed += h, h => Watcher.Changed -= h)
				.Select(x => x.EventArgs);

			Renamed = Observable
				.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(h => Watcher.Renamed += h, h => Watcher.Renamed -= h)
				.Select(x => x.EventArgs);

			Deleted = Observable
				.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(h => Watcher.Deleted += h, h => Watcher.Deleted -= h)
				.Select(x => x.EventArgs);

			Errors = Observable
				.FromEventPattern<ErrorEventHandler, ErrorEventArgs>(h => Watcher.Error += h, h => Watcher.Error -= h)
				.Select(x => x.EventArgs);

			Created = Observable
				.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(h => Watcher.Created += h, h => Watcher.Created -= h)
				.Select(x => x.EventArgs);
		}

		/// <summary>
		///     Pass a function to configure the FileSystemWatcher as desired, this constructor will manage creating and applying
		///     the configuration.
		/// </summary>
		public RxFileSystemWatcher(Action<FileSystemWatcher> configure)
			: this(new FileSystemWatcher())
		{
			configure(Watcher);
		}

		public void Start()
		{
			Watcher.EnableRaisingEvents = true;
		}

		public void Stop()
		{
			Watcher.EnableRaisingEvents = false;
		}

		public void Dispose()
		{
			Watcher.Dispose();
		}
	}
}
