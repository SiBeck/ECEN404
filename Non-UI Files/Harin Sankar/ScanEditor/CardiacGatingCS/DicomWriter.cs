using System.Numerics; // Complex numbers for FFT operations.
using FellowOakDicom; // DICOM dataset/file types.
using FellowOakDicom.IO.Buffer; // Byte buffer wrapper for pixel data.
using MathNet.Numerics.IntegralTransforms; // FFT implementation.

namespace CardiacGating; // Project namespace.

// Reconstructs k-space frames and writes them as DICOM images.
public sealed class DicomSeriesWriter
{
    /// <summary>
    /// Reconstruct each k-space frame and persist as one DICOM instance.
    /// </summary>
    public string Write(IEnumerable<MRIFrame> frames, string destination)
    {
        Directory.CreateDirectory(destination); // Ensure output folder exists.

        List<MRIFrame> list = frames.ToList(); // Materialize input enumeration once.

        if (list.Count == 0) // Must have at least one frame.
            throw new InvalidOperationException("No frames provided to DICOM writer");

        string studyUid = DicomUIDGenerator.GenerateDerivedFromUUID().UID;  // Shared study UID.
        string seriesUid = DicomUIDGenerator.GenerateDerivedFromUUID().UID; // Shared series UID.

        foreach (MRIFrame frame in list) // Write one DICOM file per frame.
        {
            ushort[,] image = ReconstructToUInt16(frame.Dataset); // k-space -> grayscale image.

            DicomDataset dataset = BuildDataset(
                image,
                frame.FrameId,
                frame.TimestampMs,
                studyUid,
                seriesUid); // Build DICOM tags + pixel data.

            string filePath = Path.Combine(destination, $"frame_{frame.FrameId:D4}.dcm"); // Stable sortable filename.
            new DicomFile(dataset).Save(filePath); // Persist DICOM to disk.
        }

        return destination; // Return output folder path.
    }

    // Builds a minimal secondary-capture dataset from one reconstructed image.
    private static DicomDataset BuildDataset(
        ushort[,] image,
        int frameIndex,
        double timestampMs,
        string studyUid,
        string seriesUid)
    {
        int rows = image.GetLength(0); // Image height.
        int cols = image.GetLength(1); // Image width.

        string sopInstanceUid = DicomUIDGenerator.GenerateDerivedFromUUID().UID; // Unique UID per file.

        var dataset = new DicomDataset(); // New dataset container.

        dataset.Add(DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage); // Storage SOP class.
        dataset.Add(DicomTag.SOPInstanceUID, sopInstanceUid);                      // Unique SOP instance UID.
        dataset.Add(DicomTag.StudyInstanceUID, studyUid);                          // Shared study UID.
        dataset.Add(DicomTag.SeriesInstanceUID, seriesUid);                        // Shared series UID.
        dataset.Add(DicomTag.Modality, "MR");                                     // Modality label.
        dataset.Add(DicomTag.PatientName, "MRDSolutions^Scan");                   // Placeholder patient name.
        dataset.Add(DicomTag.PatientID, "MRDSOL");                                // Placeholder patient ID.
        dataset.Add(DicomTag.InstanceNumber, frameIndex + 1);                      // 1-based instance number.
        dataset.Add(DicomTag.TriggerTime, $"{timestampMs:0.000}");                // Trigger time text.
        dataset.Add(DicomTag.AcquisitionTime, FormatAcquisitionTime(timestampMs)); // DICOM TM string.
        dataset.Add(DicomTag.Rows, (ushort)rows);                                  // Rows tag.
        dataset.Add(DicomTag.Columns, (ushort)cols);                               // Columns tag.
        dataset.Add(DicomTag.SamplesPerPixel, (ushort)1);                          // Grayscale.
        dataset.Add(DicomTag.PhotometricInterpretation, "MONOCHROME2");           // Monochrome display.
        dataset.Add(DicomTag.BitsAllocated, (ushort)16);                           // 16 bits allocated.
        dataset.Add(DicomTag.BitsStored, (ushort)16);                              // 16 bits stored.
        dataset.Add(DicomTag.HighBit, (ushort)15);                                 // Highest bit index.
        dataset.Add(DicomTag.PixelRepresentation, (ushort)0);                      // Unsigned pixel values.
        dataset.Add(DicomTag.PixelSpacing, new[] { "1.0", "1.0" });              // Placeholder spacing.

        byte[] pixelBytes = new byte[rows * cols * 2]; // uint16 pixels -> 2 bytes each.

        for (int r = 0; r < rows; r++) // Row loop.
        for (int c = 0; c < cols; c++) // Column loop.
        {
            int idx = (r * cols + c) * 2; // Byte index for current pixel.
            ushort val = image[r, c];      // Pixel intensity.

            pixelBytes[idx] = (byte)(val & 0xFF);       // Little-endian low byte.
            pixelBytes[idx + 1] = (byte)(val >> 8);     // Little-endian high byte.
        }

        DicomPixelData pixelData = DicomPixelData.Create(dataset, true); // Attach pixel container.
        pixelData.AddFrame(new MemoryByteBuffer(pixelBytes));            // Single-frame image payload.

        var file = new DicomFile(dataset); // Build file wrapper for meta header setup.
        file.FileMetaInfo.TransferSyntax = DicomTransferSyntax.ExplicitVRLittleEndian; // Explicit VR LE transfer syntax.
        file.FileMetaInfo.MediaStorageSOPClassUID = DicomUID.SecondaryCaptureImageStorage; // Meta SOP class.
        file.FileMetaInfo.MediaStorageSOPInstanceUID = new DicomUID(sopInstanceUid, DicomUidType.SOPInstance, false); // Meta SOP instance UID.

        return file.Dataset; // Return configured dataset.
    }

    // Performs 2D FFT reconstruction and scales magnitudes into uint16 grayscale.
    private static ushort[,] ReconstructToUInt16(Complex[,] kspace)
    {
        int rows = kspace.GetLength(0); // K-space row count.
        int cols = kspace.GetLength(1); // K-space column count.

        var work = new Complex[rows * cols]; // Flat working buffer.

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            work[r * cols + c] = kspace[r, c]; // Flatten 2D matrix into row-major 1D buffer.

        var rowBuf = new Complex[cols]; // Temporary row buffer.

        for (int r = 0; r < rows; r++) // Row FFT pass.
        {
            Array.Copy(work, r * cols, rowBuf, 0, cols); // Copy row from work buffer.
            Fourier.Forward(rowBuf, FourierOptions.NoScaling); // FFT row in place.
            Array.Copy(rowBuf, 0, work, r * cols, cols); // Copy transformed row back.
        }

        var colBuf = new Complex[rows]; // Temporary column buffer.

        for (int c = 0; c < cols; c++) // Column FFT pass.
        {
            for (int r = 0; r < rows; r++)
                colBuf[r] = work[r * cols + c]; // Gather column values.

            Fourier.Forward(colBuf, FourierOptions.NoScaling); // FFT column in place.

            for (int r = 0; r < rows; r++)
                work[r * cols + c] = colBuf[r]; // Scatter transformed column back.
        }

        Complex[] shifted = FftShift2D(work, rows, cols); // Move DC component to image center.

        double maxVal = 0.0; // Tracks max magnitude for normalization.
        var mag = new double[rows * cols]; // Magnitude image in flat form.

        for (int i = 0; i < shifted.Length; i++)
        {
            double m = shifted[i].Magnitude;      // Complex magnitude.
            mag[i] = double.IsFinite(m) ? m : 0.0; // Replace NaN/Inf with 0.
            if (mag[i] > maxVal)
                maxVal = mag[i]; // Update maximum.
        }

        var result = new ushort[rows, cols]; // Final uint16 image matrix.

        if (maxVal > 0) // Normalize only when non-zero dynamic range exists.
        {
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                long scaled = (long)(mag[r * cols + c] / maxVal * 65535.0); // Scale to full 16-bit range.
                result[r, c] = (ushort)Math.Clamp(scaled, 0L, 65535L);       // Clamp to uint16 bounds.
            }
        }

        return result; // Return reconstructed grayscale image.
    }

    // 2D fftshift equivalent for flat row-major matrix storage.
    private static Complex[] FftShift2D(Complex[] data, int rows, int cols)
    {
        var shifted = new Complex[rows * cols]; // Output buffer.
        int halfR = rows / 2;                   // Half-row shift.
        int halfC = cols / 2;                   // Half-column shift.

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int dstR = (r + halfR) % rows; // Wrapped destination row.
            int dstC = (c + halfC) % cols; // Wrapped destination col.
            shifted[dstR * cols + dstC] = data[r * cols + c]; // Move sample to shifted quadrant.
        }

        return shifted; // Return shifted buffer.
    }

    // Converts milliseconds to DICOM TM string format HHMMSS.FFF.
    private static string FormatAcquisitionTime(double timestampMs)
    {
        double total = Math.Max(0.0, timestampMs / 1000.0); // Convert ms -> s and clamp non-negative.
        int hours = (int)(total / 3600) % 24;               // Hour component.
        int minutes = (int)(total % 3600 / 60);             // Minute component.
        double seconds = total % 60.0;                      // Second component with fraction.
        return $"{hours:D2}{minutes:D2}{seconds:06.3f}";    // Compose TM string.
    }
}
