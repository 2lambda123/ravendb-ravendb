﻿using System;
using System.Collections.Generic;
using System.Text;
using Sparrow.Collections;
using Xunit;

namespace FastTests.Sparrow
{
    public class RingBufferTests
    {
        [Fact]
        public void RingBuffer_SingleItemPush()
        {
            var rb = new SingleConsumerRingBuffer<long>(1);

            long item = 910;
            Assert.True(rb.TryPush(ref item));
            Assert.False(rb.TryPush(ref item));

            var span = rb.Acquire();
            Assert.Equal(1, span.Length);
            Assert.Equal(910, span[0].Item);

            item = 911;
            Assert.False(rb.TryPush(ref item));
            rb.Release();

            Assert.True(rb.TryPush(ref item));

            span = rb.Acquire();
            Assert.Equal(1, span.Length);
            Assert.Equal(911, span[0].Item);
            rb.Release();
        }

        [Fact]
        public void RingBuffer_MultipleItemsPush()
        {
            var rb = new SingleConsumerRingBuffer<long>(4);

            long item = 910;
            Assert.True(rb.TryPush(ref item));
            Assert.True(rb.TryPush(ref item));

            var span = rb.Acquire();
            Assert.Equal(2, span.Length);
            Assert.Equal(910, span[0].Item);
            Assert.Equal(910, span[1].Item);

            item = 911;
            Assert.True(rb.TryPush(ref item));
            Assert.True(rb.TryPush(ref item));
            Assert.False(rb.TryPush(ref item));

            rb.Release();

            item = 912;
            Assert.True(rb.TryPush(ref item));

            span = rb.Acquire();
            Assert.Equal(2, span.Length);
            Assert.Equal(911, span[0].Item);
            Assert.Equal(911, span[1].Item);
            rb.Release();

            span = rb.Acquire();
            Assert.Equal(1, span.Length);
            Assert.Equal(912, span[0].Item);
            rb.Release();
        }
    }
}
