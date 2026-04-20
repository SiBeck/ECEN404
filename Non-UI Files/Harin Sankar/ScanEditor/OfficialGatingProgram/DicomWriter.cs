using FellowOakDicom;
using FellowOakDicom.IO.Buffer;

namespace OfficialGatingProgram;

public sealed class DicomWriter
{
    public void Write(
        float[,] magnitude,
        string outputPath,
        string patientName = "PHANTOM",
        string studyDescription = "Object Motion Gating")
    {
        int rows = magnitude.GetLength(0);
        int cols = magnitude.GetLength(1);

        ushort[] pixels = new ushort[rows * cols];
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
            pixels[row * cols + col] = (ushort)Math.Clamp((int)(magnitude[row, col] * 65535f), 0, 65535);

        byte[] pixelBytes = new byte[pixels.Length * 2];
        Buffer.BlockCopy(pixels, 0, pixelBytes, 0, pixelBytes.Length);

        var dataset = new DicomDataset();
        dataset.AddOrUpdate(DicomTag.MediaStorageSOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
        dataset.AddOrUpdate(DicomTag.MediaStorageSOPInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.TransferSyntaxUID, "1.2.840.10008.1.2.1");

        dataset.AddOrUpdate(DicomTag.PatientName, patientName);
        dataset.AddOrUpdate(DicomTag.PatientID, "MOTION_001");
        dataset.AddOrUpdate(DicomTag.StudyDescription, studyDescription);
        dataset.AddOrUpdate(DicomTag.SeriesDescription, "Stillness-Gated Reconstruction");
        dataset.AddOrUpdate(DicomTag.Modality, "MR");
        dataset.AddOrUpdate(DicomTag.StudyInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.SOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
        dataset.AddOrUpdate(DicomTag.SOPInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.StudyDate, DateTime.UtcNow.ToString("yyyyMMdd"));
        dataset.AddOrUpdate(DicomTag.StudyTime, DateTime.UtcNow.ToString("HHmmss"));

        dataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)1);
        dataset.AddOrUpdate(DicomTag.PhotometricInterpretation, "MONOCHROME2");
        dataset.AddOrUpdate(DicomTag.Rows, (ushort)rows);
        dataset.AddOrUpdate(DicomTag.Columns, (ushort)cols);
        dataset.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
        dataset.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
        dataset.AddOrUpdate(DicomTag.HighBit, (ushort)15);
        dataset.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
        dataset.AddOrUpdate(new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixelBytes)));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        new DicomFile(dataset).Save(outputPath);
    }
}
