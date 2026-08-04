using System;
using System.IO;
using System.Reflection;
using HonestFlow.Infrastructure.Updates;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class SelfUpdateServiceTests
    {
        [Theory]
        [InlineData(@"C:\Apps\HonestFlow.exe")]
        [InlineData(@"C:\Apps\HonestFlow (1).exe")]
        [InlineData(@"C:\Apps\HonestFlow (27).exe")]
        [InlineData(@"C:\Apps\Renamed.exe")]
        public void ResolveCanonicalUpdateTarget_AlwaysUsesCanonicalFileName(string runningExe)
        {
            string target = (string)InvokeStatic(
                "ResolveCanonicalUpdateTarget",
                runningExe);

            Assert.Equal(@"C:\Apps\HonestFlow.exe", target);
        }

        [Fact]
        public void BuildUpdateScript_WaitsForRunningCopyButReplacesCanonicalExe()
        {
            string script = (string)InvokeStatic(
                "BuildUpdateScript",
                @"C:\Users\Test\Downloads\HonestFlow (1).exe",
                @"C:\Users\Test\Downloads\HonestFlow.exe",
                @"C:\ProgramData\HonestFlow\update\HonestFlow.new.exe",
                @"C:\ProgramData\HonestFlow\update\backup",
                @"C:\ProgramData\HonestFlow\update\backup\HonestFlow.backup.exe",
                @"C:\Users\Test\Downloads",
                @"C:\ProgramData\HonestFlow\update\apply_update.log",
                1234);

            Assert.Contains("$pidToWait = 1234", script);
            Assert.Contains("$runningExe = 'C:\\Users\\Test\\Downloads\\HonestFlow (1).exe'", script);
            Assert.Contains("$targetExe = 'C:\\Users\\Test\\Downloads\\HonestFlow.exe'", script);
            Assert.Contains("Copy-Item -LiteralPath $newExe -Destination $targetExe -Force", script);
            Assert.Contains("Start-Process -FilePath $targetExe", script);
            Assert.Contains("$backupCreated", script);
            Assert.DoesNotContain("-Destination $runningExe", script);
        }

        [Fact]
        public void BuildUpdateScript_EscapesApostrophesInPaths()
        {
            string script = (string)InvokeStatic(
                "BuildUpdateScript",
                @"C:\O'Brien\HonestFlow (1).exe",
                @"C:\O'Brien\HonestFlow.exe",
                @"C:\Temp\new.exe",
                @"C:\Temp\backup",
                @"C:\Temp\backup\old.exe",
                @"C:\O'Brien",
                @"C:\Temp\update.log",
                7);

            Assert.Contains(@"C:\O''Brien\HonestFlow.exe", script);
        }

        [Theory]
        [InlineData("v3.1.2", "3.1.2")]
        [InlineData(" 3.1.2.4 ", "3.1.2.4")]
        [InlineData("broken", "0.0.0.0")]
        public void NormalizeVersion_HandlesPublishedVersionText(string source, string expected)
        {
            var version = (Version)InvokeStatic("NormalizeVersion", source);
            Assert.Equal(expected, version.ToString());
        }

        [Theory]
        [InlineData("release-3.2.1", "3.2.1")]
        [InlineData("HonestFlow_3.2.1.7", "3.2.1.7")]
        [InlineData("latest", null)]
        public void ExtractVersion_HandlesReleaseFolderNames(string source, string expected)
        {
            Assert.Equal(expected, (string)InvokeStatic("ExtractVersion", source));
        }

        private static object InvokeStatic(string name, params object[] arguments)
        {
            MethodInfo method = typeof(SelfUpdateService).GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method.Invoke(null, arguments);
        }
    }
}
