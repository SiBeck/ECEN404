using FellowOakDicom;
using FellowOakDicom.IO.Buffer;

namespace CardiacGatingMRI;

public sealed class DicomWriter
{
    public void Write(
        float[,] magnitude,
        string outputPath,
        string patientName = "PHANTOM",
        string studyDescription = "Cardiac Gating Demo")
    {
        int npe = magnitude.GetLength(0);
        int nfe = magnitude.GetLength(1);

        ushort[] pixels = new ushort[npe * nfe];
        for (int row = 0; row < npe; row++)
        for (int col = 0; col < nfe; col++)
            pixels[row * nfe + col] = (ushort)Math.Clamp((int)(magnitude[row, col] * 65535f), 0, 65535);

        byte[] pixelBytes = new byte[pixels.Length * 2];
        Buffer.BlockCopy(pixels, 0, pixelBytes, 0, pixelBytes.Length);

        var dataset = new DicomDataset();

        dataset.AddOrUpdate(DicomTag.MediaStorageSOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
        dataset.AddOrUpdate(DicomTag.MediaStorageSOPInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.TransferSyntaxUID, "1.2.840.10008.1.2.1");

        dataset.AddOrUpdate(DicomTag.PatientName, patientName);
        dataset.AddOrUpdate(DicomTag.PatientID, "CGATE_001");
        dataset.AddOrUpdate(DicomTag.StudyDescription, studyDescription);
        dataset.AddOrUpdate(DicomTag.SeriesDescription, "Gated Reconstruction");
        dataset.AddOrUpdate(DicomTag.Modality, "MR");
        dataset.AddOrUpdate(DicomTag.StudyInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.SOPClassUID, "1.2.840.10008.5.1.4.1.1.7");
        dataset.AddOrUpdate(DicomTag.SOPInstanceUID, DicomUID.Generate().UID);
        dataset.AddOrUpdate(DicomTag.StudyDate, DateTime.UtcNow.ToString("yyyyMMdd"));
        dataset.AddOrUpdate(DicomTag.StudyTime, DateTime.UtcNow.ToString("HHmmss"));

        dataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)1);
        dataset.AddOrUpdate(DicomTag.PhotometricInterpretation, "MONOCHROME2");
        dataset.AddOrUpdate(DicomTag.Rows, (ushort)npe);
        dataset.AddOrUpdate(DicomTag.Columns, (ushort)nfe);
        dataset.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
        dataset.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
        dataset.AddOrUpdate(DicomTag.HighBit, (ushort)15);
        dataset.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);
        dataset.AddOrUpdate(new DicomOtherWord(DicomTag.PixelData, new MemoryByteBuffer(pixelBytes)));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var file = new DicomFile(dataset);
        file.Save(outputPath);
    }
}
