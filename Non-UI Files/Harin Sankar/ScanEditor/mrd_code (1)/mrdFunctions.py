import os
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.lines import Line2D

def readMRD(filename):
    fname = filename if filename.endswith(".mrd") else filename + ".mrd"

    with open(fname, "rb") as f:
        # --- Read header information (matches fread(fid,4,'int32') with 'l') ---
        head = np.fromfile(f, dtype="<i4", count=4)
        Nfe, Npe, N3d, Nslice = map(int, head)

        # MATLAB: fseek(fid, 2, 0)  % 0 => 'cof' (current position)
        # So move 2 bytes forward from current position
        f.seek(2, 1)  # whence=1 => from current position

        # MATLAB: dattype = fread(fid, 1, 'int16');
        dattype_val = np.fromfile(f, dtype="<i2", count=1)[0]
        dattype_hex = f"{dattype_val:X}".zfill(2)  # emulate dec2hex -> 2-char hex string

        # MATLAB: fseek(fid, 132, 0);  % again 'cof' => from current
        f.seek(132, 1)

        # MATLAB: dim = fread(fid, 2, 'int32');
        dim = np.fromfile(f, dtype="<i4", count=2)
        Nechoes, Nexps = map(int, dim)

        # MATLAB:
        # cl = ftell(fid);
        # fseek(fid, 256-cl, 0);
        cl = f.tell()
        f.seek(256 - cl, 1)

        # --- Get text description ---
        comms_bytes = f.read(256)
        comms = comms_bytes.decode("ascii", errors="ignore").rstrip("\x00")

        # --- Read data ---
        numpts = Nfe * Npe * N3d * Nslice * Nechoes * Nexps

        is_complex = (dattype_hex[0] == "1")
        dtype_code = dattype_hex[1]

        # Map MATLAB numeric type codes to numpy dtypes (little-endian)
        if dtype_code == "0":
            base_dtype = np.dtype("<u1")   # 'uchar'
        elif dtype_code == "1":
            base_dtype = np.dtype("<i1")   # 'schar'
        elif dtype_code in ("2", "3"):
            base_dtype = np.dtype("<i2")   # 'short' / 'int16'
        elif dtype_code == "4":
            base_dtype = np.dtype("<i4")   # 'long' (assumed 32-bit)
        elif dtype_code == "5":
            base_dtype = np.dtype("<f4")   # 'float32'
        elif dtype_code == "6":
            base_dtype = np.dtype("<f8")   # 'double'
        else:
            raise ValueError(f"Unknown dattype second nibble: {dtype_code} (hex={dattype_hex})")

        if numpts == 0:
            data = np.array([], dtype=base_dtype)
        else:
            if not is_complex:
                # Real data
                arr = np.fromfile(f, dtype=base_dtype, count=numpts)
                if arr.size != numpts:
                    raise IOError("Unexpected end of file when reading real data.")
                data = arr.reshape(
                    (Nfe, Npe, N3d, Nslice, Nechoes, Nexps),
                    order="F"  # MATLAB column-major
                )
            else:
                # Complex data: interleaved real, imag
                arr = np.fromfile(f, dtype=base_dtype, count=numpts * 2)
                if arr.size != numpts * 2:
                    raise IOError("Unexpected end of file when reading complex data.")
                real = arr[0::2]
                imag = arr[1::2]
                cplx = real.astype(np.float64) + 1j * imag.astype(np.float64)
                data = cplx.reshape(
                    (Nfe, Npe, N3d, Nslice, Nechoes, Nexps),
                    order="F"
                )

        # --- Filename/path info (120 bytes) ---
        fn_bytes = f.read(120)
        # fn = fn_bytes.decode("ascii", errors="ignore").rstrip("\x00")  # if you ever need it

        # --- Remaining header parameters ---
        pars_bytes = f.read()
        pars = pars_bytes.decode("ascii", errors="ignore")

    # If data is only 2d, remove other demensions
    if N3d == 1 and Nslice == 1 and Nexps == 1:
        data = data[:, :, 0, 0, 0, 0]
    return data, Nfe, Npe, N3d, Nslice, Nechoes, Nexps, pars, comms

def imageSNR(image, signalMask, noiseMask, createPlot=False, title = None, printSNR=False):
    signalRegion = image[signalMask > 0]
    noiseRegion = image[noiseMask > 0]

    signalMean = np.mean(np.abs(signalRegion))
    noiseStd = np.std(np.abs(noiseRegion))
    noiseMagnitude = np.mean(np.abs(noiseRegion))

    snr = signalMean / noiseStd

    if createPlot:
        plt.figure(figsize=(8, 8))
        signalColor = 'cyan'
        noiseColor = 'r'
        if title is not None:
            plt.title(title + f" - SNR: {snr:.2f}", fontsize=20)
        else:
            plt.title(f"SNR - {snr:.2f}", fontsize=20)
        plt.imshow(np.abs(image), cmap='gray', aspect="auto")
        plt.contour(signalMask, colors=signalColor, linewidths=1)
        plt.contour(noiseMask, colors=noiseColor, linewidths=1)
        # --- Legend proxies ---
        legend_elements = [
            Line2D([0], [0], color=signalColor, lw=3, label='Signal Region'),
            Line2D([0], [0], color=noiseColor, lw=3, label='Noise Region')
        ]

        plt.legend(handles=legend_elements, loc='lower right')

    if printSNR:
        print(f"SNR: {snr:.2f}, Signal Mean: {signalMean:.2f}, Noise Std: {noiseStd:.2f}", "Noise Magnitude:", f"{noiseMagnitude:.2f}")
    return snr, signalMean, noiseStd

def hamming2d(arr):
    Nfe, Npe = arr.shape
    wx = np.hamming(Nfe)
    wy = np.hamming(Npe)
    window2d = np.outer(wx, wy)
    return arr * window2d

def zeroPad2d(arr, newNfe, newNpe):
    Nfe, Npe = arr.shape
    padFeBefore = (newNfe - Nfe) // 2
    padFeAfter = newNfe - Nfe - padFeBefore
    padPeBefore = (newNpe - Npe) // 2
    padPeAfter = newNpe - Npe - padPeBefore

    paddedArr = np.pad(arr,
                       ((padFeBefore, padFeAfter),
                        (padPeBefore, padPeAfter)),
                       mode='constant', constant_values=0)
    return paddedArr

if __name__ == "__main__":
    testFilePath = "C:\\Users\\benjm\\OneDrive\\Desktop\\MRSL\\Python_Scripts\\reconstructMRD\\mrdFiles\\variableSaddleCoil\\00082.mrd"
    kspace, Nfe, Npe, N3d, Nslice, Nechoes, Nexps, pars, comms = readMRD(testFilePath)

    print("Shape:", kspace.shape)
    print("Nfe, Npe, N3d, Nslice, Nechoes, Nexps =",
        Nfe, Npe, N3d, Nslice, Nechoes, Nexps)

    # kspace = hamming2d(kspace)
    # kspace = zeroPad2d(kspace, 128, 128)
    img = np.abs(np.fft.fftshift(np.fft.fft2(kspace)))

    # do a 2d plot of data
    plt.figure(figsize=(8, 8))
    plt.title("Kspace Data")
    plt.imshow(np.abs(kspace), cmap='gray', aspect="auto")
    plt.figure(figsize=(8, 8))
    plt.title("2H Image - Ryan", fontsize=20)
    plt.imshow(np.abs(img), cmap='gray', aspect="auto")
    
    # Creating SNR mask regions and plotting image showing SNR regions
    signalMask = np.zeros_like(img, dtype=bool)
    noiseMask = np.zeros_like(img, dtype=bool)
    signalMask[33:40, 15:20] = True  # Example signal region
    noiseMask[23:30, 3:20] = True     # Example noise region
    snr_value, signal, noise = imageSNR(img, signalMask, noiseMask, createPlot=True, printSNR=True)
    print(snr_value)
    print(f"Calculated SNR: {snr_value:.2f}")
    
    plt.show()

