using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests;

public sealed class KktBootstrapProcessClientTests
{
    [Fact]
    public void HelperProcess_UsesUtf8ForRussianProtocolMessages()
    {
        MethodInfo method = typeof(KktBootstrapProcessClient).GetMethod(
            "CreateStartInfo",
            BindingFlags.NonPublic | BindingFlags.Static);

        var startInfo = (ProcessStartInfo)method.Invoke(null, new object[] { "helper.exe" });

        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardOutputEncoding.WebName);
        Assert.Equal(Encoding.UTF8.WebName, startInfo.StandardErrorEncoding.WebName);
    }
}
