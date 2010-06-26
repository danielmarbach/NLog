// 
// Copyright (c) 2004-2010 Jaroslaw Kowalski <jaak@jkowalski.net>
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog.UnitTests.Targets.Wrappers
{
    using System;
    using System.Threading;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using NLog.Common;
    using NLog.Internal;
    using NLog.Targets.Wrappers;

    [TestClass]
    public class AsyncRequestQueueTests : NLogTestBase
	{
        [TestMethod]
        public void AsyncRequestQueueWithDiscardBehaviorTest()
        {
            var ev1 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev2 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev3 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev4 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });

            var queue = new AsyncRequestQueue(3, AsyncTargetWrapperOverflowAction.Discard);
            Assert.AreEqual(3, queue.RequestLimit);
            Assert.AreEqual(AsyncTargetWrapperOverflowAction.Discard, queue.OnOverflow);
            Assert.AreEqual(0, queue.RequestCount);
            queue.Enqueue(ev1);
            Assert.AreEqual(1, queue.RequestCount);
            queue.Enqueue(ev2);
            Assert.AreEqual(2, queue.RequestCount);
            queue.Enqueue(ev3);
            Assert.AreEqual(3, queue.RequestCount);
            queue.Enqueue(ev4);
            Assert.AreEqual(3, queue.RequestCount);

            AsyncLogEventInfo[] logEventInfos;

            int result = queue.DequeueBatch(10, out logEventInfos);
            Assert.AreEqual(result, logEventInfos.Length);

            Assert.AreEqual(3, result);
            Assert.AreEqual(0, queue.RequestCount);

            // ev1 is lost
            Assert.AreSame(logEventInfos[0].LogEvent, ev2.LogEvent);
            Assert.AreSame(logEventInfos[1].LogEvent, ev3.LogEvent);
            Assert.AreSame(logEventInfos[2].LogEvent, ev4.LogEvent);
            Assert.AreSame(logEventInfos[0].Continuation, ev2.Continuation);
            Assert.AreSame(logEventInfos[1].Continuation, ev3.Continuation);
            Assert.AreSame(logEventInfos[2].Continuation, ev4.Continuation);
        }

        [TestMethod]
        public void AsyncRequestQueueWithGrowBehaviorTest()
        {
            var ev1 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev2 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev3 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev4 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            
            var queue = new AsyncRequestQueue(3, AsyncTargetWrapperOverflowAction.Grow);
            Assert.AreEqual(3, queue.RequestLimit);
            Assert.AreEqual(AsyncTargetWrapperOverflowAction.Grow, queue.OnOverflow);
            Assert.AreEqual(0, queue.RequestCount);
            queue.Enqueue(ev1);
            Assert.AreEqual(1, queue.RequestCount);
            queue.Enqueue(ev2);
            Assert.AreEqual(2, queue.RequestCount);
            queue.Enqueue(ev3);
            Assert.AreEqual(3, queue.RequestCount);
            queue.Enqueue(ev4);
            Assert.AreEqual(4, queue.RequestCount);

            AsyncLogEventInfo[] logEventInfos;

            int result = queue.DequeueBatch(10, out logEventInfos);
            Assert.AreEqual(result, logEventInfos.Length);

            Assert.AreEqual(4, result);
            Assert.AreEqual(0, queue.RequestCount);

            // ev1 is lost
            Assert.AreSame(logEventInfos[0].LogEvent, ev1.LogEvent);
            Assert.AreSame(logEventInfos[1].LogEvent, ev2.LogEvent);
            Assert.AreSame(logEventInfos[2].LogEvent, ev3.LogEvent);
            Assert.AreSame(logEventInfos[3].LogEvent, ev4.LogEvent);
            Assert.AreSame(logEventInfos[0].Continuation, ev1.Continuation);
            Assert.AreSame(logEventInfos[1].Continuation, ev2.Continuation);
            Assert.AreSame(logEventInfos[2].Continuation, ev3.Continuation);
            Assert.AreSame(logEventInfos[3].Continuation, ev4.Continuation);
        }

#if !NET_CF
        [TestMethod]
        public void AsyncRequestQueueWithBlockBehavior()
        {
            var queue = new AsyncRequestQueue(10, AsyncTargetWrapperOverflowAction.Block);

            ManualResetEvent producerFinished = new ManualResetEvent(false);

            int pushingEvent = 0;

            ThreadPool.QueueUserWorkItem(
                s =>
                {
                    // producer thread
                    for (int i = 0; i < 1000; ++i)
                    {
                        AsyncLogEventInfo logEvent = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
                        logEvent.LogEvent.Message = "msg" + i;
                        
                        // Console.WriteLine("Pushing event {0}", i);
                        pushingEvent = i;
                        queue.Enqueue(logEvent);
                    }

                    producerFinished.Set();
                });

            // consumer thread
            AsyncLogEventInfo[] logEventInfos;
            int total = 0;

            while (total < 500)
            {
                int left = 500 - total;

                int got = queue.DequeueBatch(left, out logEventInfos);
                Assert.IsTrue(got <= queue.RequestLimit);
                total += got;
            }

            Thread.Sleep(500);

            // producer is blocked on trying to push event #510
            Assert.AreEqual(510, pushingEvent);
            queue.DequeueBatch(1, out logEventInfos);
            total++;
            Thread.Sleep(500);

            // producer is now blocked on trying to push event #511

            Assert.AreEqual(511, pushingEvent);
            while (total < 1000)
            {
                int left = 1000 - total;

                int got = queue.DequeueBatch(left, out logEventInfos);
                Assert.IsTrue(got <= queue.RequestLimit);
                total += got;
            }

            // producer should now finish
            producerFinished.WaitOne();
        }
#endif

        [TestMethod]
        public void AsyncRequestQueueClearTest()
        {
            var ev1 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev2 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev3 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });
            var ev4 = LogEventInfo.CreateNullEvent().WithContinuation(ex => { });

            var queue = new AsyncRequestQueue(3, AsyncTargetWrapperOverflowAction.Grow);
            Assert.AreEqual(3, queue.RequestLimit);
            Assert.AreEqual(AsyncTargetWrapperOverflowAction.Grow, queue.OnOverflow);
            Assert.AreEqual(0, queue.RequestCount);
            queue.Enqueue(ev1);
            Assert.AreEqual(1, queue.RequestCount);
            queue.Enqueue(ev2);
            Assert.AreEqual(2, queue.RequestCount);
            queue.Enqueue(ev3);
            Assert.AreEqual(3, queue.RequestCount);
            queue.Enqueue(ev4);
            Assert.AreEqual(4, queue.RequestCount);
            queue.Clear();
            Assert.AreEqual(0, queue.RequestCount);

            AsyncLogEventInfo[] logEventInfos;

            int result = queue.DequeueBatch(10, out logEventInfos);
            Assert.AreEqual(result, logEventInfos.Length);

            Assert.AreEqual(0, result);
            Assert.AreEqual(0, queue.RequestCount);
        }
    }
}