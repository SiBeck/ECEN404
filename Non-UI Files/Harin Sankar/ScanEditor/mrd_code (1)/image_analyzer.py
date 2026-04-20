import numpy as np
import mrdFunctions as mrd
import matplotlib.pyplot as plt

folder = './'
file1 = 'te014'

kspace1, *_ = mrd.readMRD(folder + file1)
img1 = np.abs(np.fft.fftshift(np.fft.fft2(kspace1)))

plt.figure(figsize=(8, 8))
plt.title(f"{file1} Image")
plt.imshow(img1, cmap="gray", aspect="auto")
plt.show()
