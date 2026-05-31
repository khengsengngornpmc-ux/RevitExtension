using System;
using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class RevitExecutionQueue
    {
        private readonly object _syncRoot = new object();
        private readonly Queue<IRevitExecutionRequest> _queue = new Queue<IRevitExecutionRequest>();

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _queue.Count;
                }
            }
        }

        public void Enqueue(IRevitExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (_syncRoot)
            {
                _queue.Enqueue(request);
            }
        }

        public bool TryDequeue(out IRevitExecutionRequest request)
        {
            lock (_syncRoot)
            {
                if (_queue.Count == 0)
                {
                    request = null;
                    return false;
                }

                request = _queue.Dequeue();
                return true;
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _queue.Clear();
            }
        }
    }
}
