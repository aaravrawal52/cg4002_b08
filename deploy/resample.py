import numpy as np


def resample_segment(arr, target_len):
    """Stretch or squash a segment to a fixed number of rows, one channel at a time.

    args:
    - arr: (n, 8) array, one row per sample
    - target_len: number of rows in the output

    returns:
    - (target_len, 8) float32 array; first and last rows equal the input's
    """
    arr = np.asarray(arr, dtype=np.float64)
    n = arr.shape[0]
    if n < 2:
        raise ValueError(f"Need at least 2 rows to resample, got {n}")

    old_axis = np.linspace(0, 1, n)            # where each input row sits, as a fraction of the segment
    new_axis = np.linspace(0, 1, target_len)   # where each output row is read

    channels = [np.interp(new_axis, old_axis, arr[:, c]) for c in range(arr.shape[1])]
    return np.stack(channels, axis=1).astype(np.float32)
