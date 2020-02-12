// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

#nullable enable

namespace Microsoft.Docs.Build
{
    internal struct InterProcessMutex : IDisposable
    {
        private static readonly AsyncLocal<ImmutableStack<string>> t_mutexRecursionStack = new AsyncLocal<ImmutableStack<string>>();

        private Mutex _mutex;

        public static InterProcessMutex Create(string mutexName)
        {
            // avoid nested mutex with same mutex name
            var stack = t_mutexRecursionStack.Value ??= ImmutableStack<string>.Empty;
            if (stack.Contains(mutexName))
            {
                throw new ApplicationException($"Nested mutex detected, mutex name: {mutexName}");
            }
            t_mutexRecursionStack.Value = stack.Push(mutexName);

            var mutex = new Mutex(initiallyOwned: false, $"Global\\ipm-{HashUtility.GetMd5Hash(mutexName)}");

            try
            {
                while (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                {
                    Log.Important($"Waiting for another process to access '{mutexName}'", ConsoleColor.Yellow);
                }
            }
            catch (AbandonedMutexException)
            {
                // When another process/thread exited without releasing its mutex,
                // this exception is thrown and we've successfully acquired the mutex.
            }

            return new InterProcessMutex { _mutex = mutex };
        }

        public void Dispose()
        {
            var stack = t_mutexRecursionStack.Value ?? throw new InvalidOperationException();
            t_mutexRecursionStack.Value = stack.Pop();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}