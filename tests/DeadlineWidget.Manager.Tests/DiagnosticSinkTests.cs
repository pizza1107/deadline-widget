using System.Text;
using DeadlineWidget.Manager;

namespace DeadlineWidget.Manager.Tests;

[TestClass]
public sealed class DiagnosticSinkTests
{
    [TestMethod]
    public void Write_RotatesOversizedUtf8LogAndKeepsOneBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DeadlineWidgetV2.DiagnosticTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, "manager.log");
        var oldBytes = Enumerable.Repeat((byte)'x', 1_048_576).ToArray();
        File.WriteAllBytes(logPath, oldBytes);
        try
        {
            var sink = new FileDiagnosticSink(directory);

            sink.Write("single-instance", new IOException("activation failed"));

            CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(logPath + ".1"));
            var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(logPath));
            StringAssert.Contains(text, "single-instance");
            StringAssert.Contains(text, typeof(IOException).FullName!);
            StringAssert.Contains(text, "activation failed");
            Assert.IsTrue(DateTimeOffset.TryParse(text[..text.IndexOf(' ', StringComparison.Ordinal)], out var timestamp));
            Assert.AreEqual(TimeSpan.Zero, timestamp.Offset);
            Assert.IsFalse(File.Exists(logPath + ".2"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
