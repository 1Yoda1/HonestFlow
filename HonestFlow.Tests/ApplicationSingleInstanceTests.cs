using System;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class ApplicationSingleInstanceTests
    {
        [Fact]
        public void TryAcquire_RejectsSecondInstanceUntilFirstIsReleased()
        {
            string name = @"Local\HonestFlow.Tests." + Guid.NewGuid().ToString("N");

            Assert.True(ApplicationSingleInstance.TryAcquire(name, out var first));
            try
            {
                Assert.False(ApplicationSingleInstance.TryAcquire(name, out var second));
                Assert.Null(second);
            }
            finally
            {
                first.Dispose();
            }

            Assert.True(ApplicationSingleInstance.TryAcquire(name, out var afterRelease));
            afterRelease.Dispose();
        }
    }
}
