using System.Reflection;
using System.Runtime.InteropServices;

namespace Occlet.Approach2.LightGbm;

/// <summary>
/// Direct bindings to LightGBM's official C API (lib_lightgbm — the same native library that ML.NET's
/// LightGBM trainer and the Python package wrap). Calling it directly keeps the solution free of NuGet
/// packages and gives access to everything we need: quantile objective, TreeSHAP contributions, model files.
/// Get the library with tools/get-lightgbm.ps1 (Windows) or tools/get-lightgbm.sh (Linux/macOS).
/// </summary>
internal static class LightGbmNative
{
    private const string Lib = "lib_lightgbm";
    public const int DtypeFloat32 = 0, DtypeFloat64 = 1;
    public const int PredictNormal = 0, PredictContrib = 3;
    public const int ImportanceSplit = 0, ImportanceGain = 1;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr LGBM_GetLastError();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_DatasetCreateFromMat(double[] data, int dataType, int nrow, int ncol, int isRowMajor,
        [MarshalAs(UnmanagedType.LPStr)] string parameters, IntPtr reference, out IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_DatasetSetField(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string fieldName,
        float[] fieldData, int numElement, int type);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_DatasetSetFeatureNames(IntPtr handle,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] names, int numFeatures);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_DatasetFree(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterCreate(IntPtr trainData, [MarshalAs(UnmanagedType.LPStr)] string parameters, out IntPtr booster);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterUpdateOneIter(IntPtr booster, out int isFinished);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterPredictForMat(IntPtr booster, double[] data, int dataType, int nrow, int ncol, int isRowMajor,
        int predictType, int startIteration, int numIteration, [MarshalAs(UnmanagedType.LPStr)] string parameter,
        out long outLen, double[] outResult);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterSaveModel(IntPtr booster, int startIteration, int numIteration, int featureImportanceType,
        [MarshalAs(UnmanagedType.LPStr)] string filename);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterFeatureImportance(IntPtr booster, int numIteration, int importanceType, double[] outResults);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int LGBM_BoosterFree(IntPtr booster);

    public static void Check(int rc)
    {
        if (rc != 0) throw new InvalidOperationException("LightGBM error: " + Marshal.PtrToStringAnsi(LGBM_GetLastError()));
    }

    /// <summary>True when lib_lightgbm can be found next to the program (or on the system path).</summary>
    public static bool IsAvailable(out string message)
    {
        if (NativeLibrary.TryLoad(Lib, Assembly.GetExecutingAssembly(), null, out _)) { message = "lib_lightgbm loaded"; return true; }
        message = "lib_lightgbm was not found. Run tools/get-lightgbm.ps1 (Windows) or tools/get-lightgbm.sh (Linux/macOS), then rebuild.";
        return false;
    }
}

/// <summary>A trained LightGBM model (one per quantile) with predict, SHAP contributions, importance and save.</summary>
internal sealed class LgbmBooster : IDisposable
{
    private IntPtr _booster, _dataset;
    public int NumFeatures { get; }

    private LgbmBooster(IntPtr booster, IntPtr dataset, int ncol) { _booster = booster; _dataset = dataset; NumFeatures = ncol; }

    public static LgbmBooster Train(double[] rowMajorX, int nrow, int ncol, float[] y, string[] featureNames,
                                    string datasetParams, string trainParams, int iterations)
    {
        LightGbmNative.Check(LightGbmNative.LGBM_DatasetCreateFromMat(rowMajorX, LightGbmNative.DtypeFloat64, nrow, ncol, 1,
            datasetParams, IntPtr.Zero, out var ds));
        LightGbmNative.Check(LightGbmNative.LGBM_DatasetSetField(ds, "label", y, y.Length, LightGbmNative.DtypeFloat32));
        LightGbmNative.Check(LightGbmNative.LGBM_DatasetSetFeatureNames(ds, featureNames, ncol));
        LightGbmNative.Check(LightGbmNative.LGBM_BoosterCreate(ds, trainParams, out var booster));
        for (int i = 0; i < iterations; i++)
        {
            LightGbmNative.Check(LightGbmNative.LGBM_BoosterUpdateOneIter(booster, out int finished));
            if (finished != 0) break;
        }
        return new LgbmBooster(booster, ds, ncol);
    }

    public double[] Predict(double[] rowMajorX, int nrow)
    {
        var result = new double[nrow];
        LightGbmNative.Check(LightGbmNative.LGBM_BoosterPredictForMat(_booster, rowMajorX, LightGbmNative.DtypeFloat64, nrow, NumFeatures, 1,
            LightGbmNative.PredictNormal, 0, -1, "num_threads=1", out _, result));
        return result;
    }

    /// <summary>TreeSHAP: per row, one contribution per feature plus the expected value (last column).</summary>
    public double[] Contributions(double[] rowMajorX, int nrow)
    {
        var result = new double[nrow * (NumFeatures + 1)];
        LightGbmNative.Check(LightGbmNative.LGBM_BoosterPredictForMat(_booster, rowMajorX, LightGbmNative.DtypeFloat64, nrow, NumFeatures, 1,
            LightGbmNative.PredictContrib, 0, -1, "num_threads=1", out _, result));
        return result;
    }

    public double[] GainImportance()
    {
        var result = new double[NumFeatures];
        LightGbmNative.Check(LightGbmNative.LGBM_BoosterFeatureImportance(_booster, -1, LightGbmNative.ImportanceGain, result));
        return result;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        LightGbmNative.Check(LightGbmNative.LGBM_BoosterSaveModel(_booster, 0, -1, LightGbmNative.ImportanceGain, path));
    }

    public void Dispose()
    {
        if (_booster != IntPtr.Zero) { LightGbmNative.LGBM_BoosterFree(_booster); _booster = IntPtr.Zero; }
        if (_dataset != IntPtr.Zero) { LightGbmNative.LGBM_DatasetFree(_dataset); _dataset = IntPtr.Zero; }
    }
}
