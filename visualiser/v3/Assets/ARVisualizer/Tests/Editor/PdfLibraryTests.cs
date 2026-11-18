using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class PdfLibraryTests
    {
        string folder;
        [SetUp] public void SetUp() => folder = Path.Combine(Application.temporaryCachePath, "PdfTest-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        [Test]
        public void CreatesEmptyFolderAndFiltersSortsAndBatchesPdfsWithoutDeletingOriginals()
        {
            LibraryResult result = null;
            new PdfLibrary(folder).RequestPage(0, value => result = value);
            Assert.IsTrue(Directory.Exists(folder)); Assert.IsTrue(result.ok); Assert.AreEqual(0, result.total);
            for (int i = 0; i < 13; i++) WriteSample(Path.Combine(folder, $"Document {i:00}.PDF"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "Ignored");
            Directory.CreateDirectory(Path.Combine(folder, "Not a file.pdf"));
            var library = new PdfLibrary(folder);
            library.RequestPage(1, value => result = value);
            Assert.AreEqual(13, result.total); Assert.AreEqual(6, result.assets.Length);
            Assert.AreEqual("Document 06.PDF", result.assets[0].caption);
            string original = result.assets[0].id;
            Assert.IsNull(result.assets[0].path, "Original PDFs must never be treated as thumbnail cache files");
            PhotoLibrary.DeleteResultFiles(result);
            Assert.IsTrue(File.Exists(original));
            library.RequestPage(2, value => result = value);
            Assert.AreEqual(1, result.assets.Length);
            library.RequestPage(int.MaxValue, value => result = value);
            Assert.IsEmpty(result.assets);
        }

        [Test]
        public void FileUrlsEscapeNamesAndRefreshReflectsExternalChanges()
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Deutsch #1 100% ä.PDF");
            WriteSample(path);
            Assert.IsTrue(PdfLibrary.TryGetFileUrl(path, out string url));
            Assert.AreEqual(Path.GetFullPath(path), new Uri(url).LocalPath);
            StringAssert.Contains("%23", url); StringAssert.Contains("%25", url);
            Assert.IsEmpty(new Uri(url).Fragment);
            File.Delete(path);
            Assert.IsFalse(PdfLibrary.TryGetFileUrl(path, out _));
            LibraryResult result = null;
            new PdfLibrary(folder).RequestPage(0, value => result = value);
            Assert.AreEqual(0, result.total);
        }

        // A valid, one-page fixture, also useful for on-device smoke testing.
        internal static void WriteSample(string path)
        {
            string stream = "BT /F1 24 Tf 40 740 Td (AR PDF test) Tj ET\n";
            string[] objects = {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Length " + stream.Length + " >>\nstream\n" + stream + "endstream"
            };
            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new int[objects.Length];
            for (int i = 0; i < objects.Length; i++) { offsets[i] = pdf.Length; pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
            int xref = pdf.Length;
            pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets) pdf.Append($"{offset:0000000000} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
        }
    }
}
