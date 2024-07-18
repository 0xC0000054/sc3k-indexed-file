////////////////////////////////////////////////////////////////////////////
//
// This file is part of sc3k-indexed-file, a utility for working with the
// indexed database file format used by SimCity 3000.
//
// Copyright (c) 2024 Nicholas Hayes
//
// This file is licensed under terms of the MIT License.
// See LICENSE.txt for more information.
//
////////////////////////////////////////////////////////////////////////////

namespace SC3KIxf
{
    internal abstract class Disposable : IDisposable
    {
        private int isDisposed;

        protected Disposable()
        {
            this.isDisposed = 0;
        }

        ~Disposable()
        {
            if (Interlocked.Exchange(ref this.isDisposed, 1) == 0)
            {
                Dispose(false);
            }
        }

        public bool IsDisposed => Thread.VolatileRead(ref this.isDisposed) == 1;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.isDisposed, 1) == 0)
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

        protected abstract void Dispose(bool disposing);

        protected void VerifyNotDisposed()
        {
            ObjectDisposedException.ThrowIf(this.IsDisposed, this);
        }
    }
}
