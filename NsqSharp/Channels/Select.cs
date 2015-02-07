﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NsqSharp.Channels
{
    /// <summary>
    /// Control structure to send or receive from the first available channel. Chain Case methods and end with a
    /// call to Default or NoDefault.
    /// </summary>
    /// <example>
    /// private static void Main()
    /// {
    ///     var c = new Chan&lt;string&gt;();
    ///     var quit = new Chan&lt;bool&gt;();
    ///
    ///     Task.Factory.StartNew(() => GetNumbers(c, "A", quit));
    ///     Task.Factory.StartNew(() => GetNumbers(c, "B", quit));
    ///     Task.Factory.StartNew(() => GetNumbers(c, "C", quit));
    ///
    ///     int count = 0;
    ///     foreach (var msg in c)
    ///     {
    ///         Console.WriteLine(msg);
    ///
    ///         if (count > 100)
    ///         {
    ///             quit.Send(true);
    ///             quit.Send(true);
    ///             quit.Send(true);
    ///         }
    ///
    ///         count++;
    ///     }
    /// }
    /// 
    /// private static void GetNumbers(Chan&lt;string&gt; c, string name, Chan&lt;bool&gt; quit)
    /// {
    ///    for (var i = 1; !breakLoop; i++)
    ///    {
    ///        Select
    ///            .CaseSend(c, string.Format("{0} {1}", name, i), () =>
    ///            {
    ///                if (name == "A")
    ///                {
    ///                    Thread.Sleep(200);
    ///                }
    ///                if (name == "C")
    ///                {
    ///                    Thread.Sleep(100);
    ///                }
    ///                Thread.Sleep(100);
    ///            })
    ///            .CaseReceive(quit, o =>
    ///            {
    ///                breakLoop = true;
    ///            })
    ///           .NoDefault();
    ///    }
    /// 
    ///    c.Close();
    /// }
    /// </example>
    public static class Select
    {
        /// <summary>
        /// Creates a case for receiving from the specific channel and assigns the Select a name for debugging.
        /// </summary>
        /// <param name="debugName">The select's name for debugging</param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to 
        /// Default or NoDefault.</returns>
        public static SelectCase DebugName(string debugName)
        {
            return new SelectCase { DebugName = debugName };
        }

        /// <summary>
        /// Creates a case for receiving from the specific channel.
        /// </summary>
        /// <param name="debugName">The channel's name for debugging.</param>
        /// <param name="c">The channel to receive from. Can be <c>null</c>.</param>
        /// <param name="func">The function to execute with the data received from the channel. Can be <c>null</c></param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to 
        /// Default or NoDefault.</returns>
        public static SelectCase CaseReceive<T>(string debugName, IReceiveOnlyChan<T> c, Action<T> func = null)
        {
            return new SelectCase().CaseReceive(debugName, c, func);
        }

        /// <summary>
        /// Creates a case for sending to the specific channel.
        /// </summary>
        /// <param name="debugName">The channel's name for debugging.</param>
        /// <param name="c">The channel to send to. Can be <c>null</c>.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="func">The callback function to execute once the message has been sent. Can be <c>null</c>.</param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public static SelectCase CaseSend<T>(string debugName, ISendOnlyChan<T> c, T message, Action func = null)
        {
            return new SelectCase().CaseSend(debugName, c, message, func);
        }

        /// <summary>
        /// Creates a case for receiving from the specific channel.
        /// </summary>
        /// <param name="c">The channel to receive from. Can be <c>null</c>.</param>
        /// <param name="func">The function to execute with the data received from the channel. Can be <c>null</c></param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to 
        /// Default or NoDefault.</returns>
        public static SelectCase CaseReceive<T>(IReceiveOnlyChan<T> c, Action<T> func = null)
        {
            return CaseReceive(null, c, func);
        }

        /// <summary>
        /// Creates a case for sending to the specific channel.
        /// </summary>
        /// <param name="c">The channel to send to. Can be <c>null</c>.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="func">The callback function to execute once the message has been sent. Can be <c>null</c>.</param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public static SelectCase CaseSend<T>(ISendOnlyChan<T> c, T message, Action func = null)
        {
            return CaseSend(null, c, message, func);
        }
    }

    /// <summary>
    /// Control structure to send or receive from the first available channel. Chain Case methods and end with a call to
    /// Default or NoDefault.
    /// </summary>
    public class SelectCase
    {
        private class ReceiveCase
        {
            public string DebugName { get; set; }
            public IReceiveOnlyChan Chan { get; set; }
        }

        private class SendCase
        {
            public string DebugName { get; set; }
            public ISendOnlyChan Chan { get; set; }
        }

        private readonly Dictionary<ReceiveCase, Action<object>> _receiveFuncs =
            new Dictionary<ReceiveCase, Action<object>>();

        private readonly Dictionary<SendCase, Tuple<Action, object>> _sendFuncs =
            new Dictionary<SendCase, Tuple<Action, object>>();

        private Action _default;
        private bool _hasDefault;

        private bool _isExecuteCalled;

        /// <summary>
        /// Name used for debugging
        /// </summary>
        public string DebugName { get; set; }

        /// <summary>
        /// Creates a case for receiving from the specific channel.
        /// </summary>
        /// <param name="debugName">The name of the channel.</param>
        /// <param name="c">The channel to receive from. Can be <c>null</c>.</param>
        /// <param name="func">The function to execute with the data received from the channel. Can be <c>null</c></param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public SelectCase CaseReceive<T>(string debugName, IReceiveOnlyChan<T> c, Action<T> func = null)
        {
            if (c != null)
            {
                _receiveFuncs.Add(
                    new ReceiveCase
                    {
                        Chan = c,
                        DebugName = (debugName != null ? "<-" + DebugName + "::" + debugName : null)
                    },
                    func == null
                        ? (Action<object>)null
                        : o => func(o != null ? (T)o : default(T))
                );
            }

            return this;
        }

        /// <summary>
        /// Creates a case for sending to the specific channel.
        /// </summary>
        /// <param name="debugName">The name of the channel.</param>
        /// <param name="c">The channel to send to. Can be <c>null</c>.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="func">The callback function to execute once the message has been sent. Can be <c>null</c>.</param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public SelectCase CaseSend<T>(string debugName, ISendOnlyChan<T> c, T message, Action func = null)
        {
            if (c != null)
            {
                _sendFuncs.Add(
                    new SendCase
                    {
                        Chan = c,
                        DebugName = (debugName != null ? DebugName + "::" + debugName + "<-" : null)
                    }
                    , new Tuple<Action, object>(func, message)
                );
            }

            return this;
        }

        /// <summary>
        /// Creates a case for receiving from the specific channel.
        /// </summary>
        /// <param name="c">The channel to receive from. Can be <c>null</c>.</param>
        /// <param name="func">The function to execute with the data received from the channel. Can be <c>null</c></param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public SelectCase CaseReceive<T>(IReceiveOnlyChan<T> c, Action<T> func = null)
        {
            return CaseReceive(null, c, func);
        }

        /// <summary>
        /// Creates a case for sending to the specific channel.
        /// </summary>
        /// <param name="c">The channel to send to. Can be <c>null</c>.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="func">The callback function to execute once the message has been sent. Can be <c>null</c>.</param>
        /// <returns>An instance to append another Case, Default, or NoDefault. Select must end with a call to
        /// Default or NoDefault.</returns>
        public SelectCase CaseSend<T>(ISendOnlyChan<T> c, T message, Action func = null)
        {
            return CaseSend(null, c, message, func);
        }

        /// <summary>
        /// Executes a default action if no channels are ready.
        /// </summary>
        /// <param name="func">The callback function to execute if no channels are ready. Can be <c>null</c>.</param>
        public void Default(Action func)
        {
            _default = func;
            _hasDefault = true;
            Execute();
        }

        /// <summary>
        /// Specifies that no action should be taken if no channels are ready. Blocks until one channel is ready and its
        /// callback function has been executed.
        /// </summary>
        public void NoDefault()
        {
            Execute();
        }

        private bool CheckCases(out Exception exception)
        {
            exception = null;

            foreach (var kvp in _receiveFuncs)
            {
                var c = kvp.Key.Chan;
                var f = kvp.Value;

                object o = null;
                bool gotValue = false;

                if (c.IsClosed)
                {
                    Debug.WriteLine("[{0}] Closed channel {1}...", GetThreadName(), kvp.Key.DebugName);
                    gotValue = true;
                }
                else
                {
                    if (c.TryLockReceive())
                    {
                        try
                        {
                            if (c.IsReadyToSend)
                            {
                                Debug.WriteLine("[{0}] Trying {1}...", GetThreadName(), kvp.Key.DebugName);
                                o = c.Receive();
                                gotValue = true;
                            }
                        }
                        finally
                        {
                            c.UnlockReceive();
                        }
                    }
                }

                if (gotValue)
                {
                    Debug.WriteLine("[{0}] {1} success.", GetThreadName(), kvp.Key.DebugName);
                    if (f != null)
                    {
                        try
                        {
                            f(o);
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }
                    return true;
                }
            }

            foreach (var kvp in _sendFuncs)
            {
                var c = kvp.Key.Chan;
                var f = kvp.Value.Item1;
                var d = kvp.Value.Item2;

                if (c.IsClosed)
                    continue;

                bool sentValue = false;
                if (c.TryLockSend())
                {
                    try
                    {
                        Debug.WriteLine("[{0}] Trying {1}...", GetThreadName(), kvp.Key.DebugName);
                        sentValue = c.TrySend(d, TimeSpan.FromMilliseconds(20));
                    }
                    finally
                    {
                        c.UnlockSend();
                    }
                }

                if (sentValue)
                {
                    Debug.WriteLine("[{0}] {1} success.", GetThreadName(), kvp.Key.DebugName);
                    if (f != null)
                    {
                        try
                        {
                            f();
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        private void Execute()
        {
            Exception caseHandlerException = null;
            AutoResetEvent ready = null;

            try
            {
                if (_isExecuteCalled)
                    throw new Exception("Default/NoDefault can only be called once per select");

                _isExecuteCalled = true;

#if DEBUG
                AddThreadToDebugLog();

                Debug.WriteLine(string.Format("[{0}] +++ Entering Select +++", GetThreadName()));
                foreach (var c in _sendFuncs.Keys)
                {
                    Debug.WriteLine("[{0}] Case: {1}", GetThreadName(), c.DebugName);
                }

                foreach (var c in _receiveFuncs.Keys)
                {
                    Debug.WriteLine("[{0}] Case: {1}", GetThreadName(), c.DebugName);
                }

                Debug.WriteLine("[{0}] Has Default: {1}", GetThreadName(), _hasDefault);
#endif

                if (_hasDefault)
                {
                    bool isDone = CheckCases(out caseHandlerException);

                    if (!isDone)
                    {
                        if (_default != null)
                        {
                            Debug.WriteLine(string.Format("[{0}] executing DEFAULT", GetThreadName()));
                            try
                            {
                                _default();
                            }
                            catch (Exception ex)
                            {
                                caseHandlerException = ex;
                            }
                        }
                    }
                }
                else
                {
                    ready = new AutoResetEvent(initialState: false);

                    foreach (var c in _sendFuncs.Keys)
                    {
                        c.Chan.AddListener(ready);
                    }

                    foreach (var c in _receiveFuncs.Keys)
                    {
                        c.Chan.AddListener(ready);
                    }

                    bool isDone = CheckCases(out caseHandlerException);

                    if (!isDone)
                    {
                        bool done;
                        do
                        {

#if DEBUG
                            bool signaled = ready.WaitOne(TimeSpan.FromMilliseconds(50));
                            if (!signaled && !string.IsNullOrEmpty(DebugName))
                            {
                                Debug.WriteLine(string.Format("[{0}] Waiting...", GetThreadName()));
                                Debug.WriteLine(string.Format("[{0}] Active threads:", GetThreadName()));
                                var activeThreads = GetActiveThreads();
                                foreach (var threadId in activeThreads)
                                {
                                    Debug.WriteLine("[{0}] Thread: {1}", GetThreadName(), threadId);
                                }
                            }
#else
                            ready.WaitOne(TimeSpan.FromMilliseconds(50));
#endif
                            done = CheckCases(out caseHandlerException);
                        } while (!done);
                    }
                }

                CleanUp(ready);

#if DEBUG
                RemoveThreadFromDebugLog();

                Debug.WriteLine(string.Format("[{0}] --- Exited select ---", GetThreadName()));
#endif
            }
            catch (Exception)
            {
                CleanUp(ready);
#if DEBUG
                RemoveThreadFromDebugLog();
#endif
            }

            if (caseHandlerException != null)
                throw caseHandlerException;
        }

#if DEBUG
        private static readonly List<string> _activeThreads = new List<string>();

        private void RemoveThreadFromDebugLog()
        {
            lock (_activeThreads)
            {
                _activeThreads.Remove(GetThreadName());
            }
        }

        private void AddThreadToDebugLog()
        {
            lock (_activeThreads)
            {
                _activeThreads.Add(GetThreadName());
            }
        }

        private string[] GetActiveThreads()
        {
            lock (_activeThreads)
            {
                return _activeThreads.ToArray();
            }
        }

#endif

        private string GetThreadName()
        {
            return string.Format("{0}/{1}", Thread.CurrentThread.ManagedThreadId, DebugName);
        }

        private void CleanUp(AutoResetEvent ready)
        {
            if (ready != null)
            {
                foreach (var c in _receiveFuncs.Keys)
                {
                    c.Chan.RemoveListener(ready);
                }

                foreach (var c in _sendFuncs.Keys)
                {
                    c.Chan.RemoveListener(ready);
                }

                ready.Dispose();
            }

            _receiveFuncs.Clear();
            _sendFuncs.Clear();

            _default = null;
        }
    }
}
