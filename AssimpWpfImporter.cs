using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Documents;
using System.Windows.Media.Media3D;
using Ab3d.Assimp;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using System.IO;

namespace Ab3d.Assimp
{
    /// <summary>
    /// AssimpWpfImporter class uses Assimp importer library to read WPF 3D models from many 3D files.
    /// </summary>
    public class AssimpWpfImporter : IDisposable
    {
        // Used to manually load C++ Redistributable
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

        private AssimpContext _assimpContext;

        private AssimpWpfConverter _assimpWpfConverter;

        private Action<string, string> _loggerCallback;

        /// <summary>
        /// File name of the 32-bit assimp library
        /// </summary>
        public static string Assimp32FileName = "Assimp32.dll";

        /// <summary>
        /// File name of the 64-bit assimp library
        /// </summary>
        public static string Assimp64FileName = "Assimp64.dll";

        private Version _assimpVersion;

        /// <summary>
        /// Gets the version of the native Assimp library.
        /// </summary>
        public Version AssimpVersion
        {
            get
            {
                if (_assimpVersion == null)
                {
                    int major, minor, revision;

                    try
                    {
                        major = (int)AssimpLibrary.Instance.GetVersionMajor();
                    }
                    catch
                    {
                        major = 0;
                    }

                    try
                    {
                        minor = (int)AssimpLibrary.Instance.GetVersionMinor();
                    }
                    catch
                    {
                        minor = 0;
                    }

                    try
                    {
                        revision = (int)AssimpLibrary.Instance.GetVersionRevision();
                    }
                    catch
                    {
                        revision = 0;
                    }

                    // Ensure that the version is not negative (this may happen for some builds)
                    major = Math.Max(0, major);
                    minor = Math.Max(0, minor);
                    revision = Math.Max(0, revision);
                    
                    _assimpVersion = new Version(major, minor, revision);
                }

                return _assimpVersion;
            }
        }


        /// <summary>
        /// Gets or sets Assimp PostProcessSteps (see assimp documentation for more information).
        /// Default value is FlipUVs | GenerateSmoothNormals | Triangulate.
        /// </summary>
        public PostProcessSteps AssimpPostProcessSteps { get; set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if PolygonIndicesProperty is set to the created MeshGeometry3D objects. 
        /// This property defines the indexes of positions that define mesh polygons.
        /// This property is used only when the assimp scene was not read with Triangulation post process and when the Ab3d.PowerToys library is referenced.
        /// </summary>
        public bool ReadPolygonIndices { get; set; }

        /// <summary>
        /// Gets or sets a default material that is used when no other material is specified.
        /// </summary>
        public System.Windows.Media.Media3D.Material DefaultMaterial
        {
            get
            {
                if (_assimpWpfConverter == null)
                    return null;

                return _assimpWpfConverter.DefaultMaterial;
            }
            set
            {
                if (_assimpWpfConverter != null)
                    _assimpWpfConverter.DefaultMaterial = value;
            }
        }

        /// <summary>
        /// Gets or sets a Boolean that specifies if we always do a convertion from left to right handed coordinate system.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>ForceConvertToRightHandedCoordinateSystem</b> gets or sets a Boolean that specifies if we always do a convertion from left to right handed coordinate system.
        /// </para>
        /// <para>
        /// WPF uses right handed coordinate system - the Z axis points away from the screen. DirectX uses left handed coordinate system - there the Z axis points into the screen.
        /// </para>
        /// </remarks>
        public bool ForceConvertToRightHandedCoordinateSystem
        {
            get
            {
                if (_assimpWpfConverter == null)
                    return false;

                return _assimpWpfConverter.ForceConvertToRightHandedCoordinateSystem;
            }
            set
            {
                if (_assimpWpfConverter != null)
                    _assimpWpfConverter.ForceConvertToRightHandedCoordinateSystem = value;
            }
        }

        /// <summary>
        /// Gets a dictionary that can be used to get a 3D object by its name (key = name, value = object)
        /// </summary>
        public Dictionary<string, object> NamedObjects { get; private set; }

        /// <summary>
        /// Gets a dictionary that can be used to get a name of a 3D object (key = object, value = name)
        /// </summary>
        public Dictionary<object, string> ObjectNames
        {
            get
            {
                if (_assimpWpfConverter == null)
                    return null;

                return _assimpWpfConverter.ObjectNames;
            }
        }

        /// <summary>
        /// Gets the Assimp's Scene object that was created when the 3D file was read.
        /// </summary>
        public Scene ImportedAssimpScene { get; private set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if this instance of AssimpWpfImporter has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }


        /// <summary>
        /// Gets or sets a logger callback action that takes two strings (message and data).
        /// </summary>
        public Action<string, string> LoggerCallback
        {
            get { return _loggerCallback; }
            set
            {
                LogStream.DetachAllLogstreams();

                if (value != null)
                {
                    _loggerCallback = value;

                    _assimpWpfConverter.LoggerCallback = value;

                    var logger = new LogStream((msg, data) => _loggerCallback(msg, data));

                    logger.Attach();
                }
            }
        }

        /// <summary>
        /// Gets or sets a boolean that specifies if verbose logging is enabled. Default value is false.
        /// </summary>
        public bool IsVerboseLoggingEnabled
        {
            get { return LogStream.IsVerboseLoggingEnabled; }
            set { LogStream.IsVerboseLoggingEnabled = value; }
        }

        /// <summary>
        /// Gets a string array of file extensions that are supported for import by Assimp.
        /// </summary>
        public string[] SupportedImportFormats
        {
            get
            {
                if (IsDisposed)
                    throw new ObjectDisposedException("AssimpWpfImporter has been disposed");

                return _assimpContext.GetSupportedImportFormats();
            }
        }

        /// <summary>
        /// Initializes a new instance of the AssimpWpfImporter class.
        /// </summary>
        public AssimpWpfImporter()
        {
            _assimpContext = new AssimpContext();
            _assimpWpfConverter = new AssimpWpfConverter();

            //AssimpPostProcessSteps = PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.Triangulate;
            AssimpPostProcessSteps = PostProcessSteps.Triangulate;
        }

        /// <summary>
        /// ReadModel3D method reads 3D models from specified file and returns the 3D models as Model3DGroup or GeomentryModel3D.
        /// </summary>
        /// <param name="fileName">file name</param>
        /// <param name="texturesPath">optional: directory name where the textures files are; if null than the same path as fileName is used</param>
        /// <returns>WPF Model3D object</returns>
        public Model3D ReadModel3D(string fileName, string texturesPath = null)
        {
            ImportedAssimpScene = ReadFileToAssimpScene(fileName);

            //bool success = _assimpContext.ExportFile(ImportedAssimpScene, @"C:\temp\AssimpExport.dae", "collada");

            if (string.IsNullOrEmpty(texturesPath))
                texturesPath = System.IO.Path.GetDirectoryName(fileName);

            return ProcessImportedScene(texturesPath, resolveResourceFunc: null);
        }

        /// <summary>
        /// ReadModel3D method reads 3D models from stream and returns the 3D models as Model3DGroup or GeomentryModel3D.
        /// When the model have additional textures, the resolveResourceFunc must be set to a method that converts the resource name into a Stream.
        /// </summary>
        /// <param name="fileStream">file stream</param>
        /// <param name="formatHint">file extension to serve as a hint to Assimp to choose which importer to use - for example ".dae"</param>
        /// <param name="resolveResourceFunc">method that converts the resource name into Stream - used to read additional resources (materials and textures)</param>
        /// <returns>WPF Model3D object</returns>
        public Model3D ReadModel3D(Stream fileStream, string formatHint, Func<string, Stream> resolveResourceFunc = null)
        {
            ImportedAssimpScene = ReadFileToAssimpScene(fileStream, formatHint);

            return ProcessImportedScene(texturesPath: null, resolveResourceFunc: resolveResourceFunc);
        }

        /// <summary>
        /// Returns the GeometryModel3D that was created from the specified assimp mesh object.
        /// </summary>
        /// <param name="mesh">assimp mesh object</param>
        /// <returns>GeometryModel3D that was created from the specified assimp mesh object</returns>
        public GeometryModel3D GetGeometryModel3DForAssimpMesh(Mesh mesh)
        {
            GeometryModel3D geometryModel3D;

            if (_assimpWpfConverter != null)
                geometryModel3D = _assimpWpfConverter.GetGeometryModel3DForAssimpMesh(mesh);
            else
                geometryModel3D = null;

            return geometryModel3D;
        }

        private Model3D ProcessImportedScene(string texturesPath, Func<string, Stream> resolveResourceFunc)
        {
            if (ImportedAssimpScene == null)
                return null;

            var usedPostProcessSteps = AssimpPostProcessSteps;
            if (ReadPolygonIndices)
                usedPostProcessSteps = usedPostProcessSteps & ~PostProcessSteps.Triangulate; // When we are reding edge lines, we need to prevent Triangulate post process step

            _assimpWpfConverter.ReadPolygonIndices = ReadPolygonIndices;

            var wpfModel = _assimpWpfConverter.ConvertAssimpModel(ImportedAssimpScene, texturesPath, resolveResourceFunc, usedPostProcessSteps);

            var objectNames = _assimpWpfConverter.ObjectNames;

            if (objectNames != null)
            {
                NamedObjects = new Dictionary<string, object>();
                foreach (KeyValuePair<object, string> objectName in objectNames)
                    NamedObjects[objectName.Value] = objectName.Key;
            }

            return wpfModel;
        }


        /// <summary>
        /// ReadFileToAssimpScene reads the specified file stream and returns Assimp's Scene object.
        /// Assimp's Scene object can be manually converted into WPF's object with AssimpWpfConverter class.
        /// </summary>
        /// <param name="fileStream">file stream</param>
        /// <param name="formatHint">file extension to serve as a hint to Assimp to choose which importer to use - for example ".dae"</param>
        /// <returns>Assimp's Scene object</returns>
        public Scene ReadFileToAssimpScene(Stream fileStream, string formatHint)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("AssimpWpfImporter has been disposed");


            if (!_assimpContext.IsImportFormatSupported(Path.GetExtension(formatHint)))
            {
                LogMessage(string.Format("File extension {0} is not supported by assimp.", Path.GetExtension(formatHint)));
                return null;
            }

            LogMessage("Start importing file stream");

            Scene assimpScene = null;

            try
            {
                assimpScene = _assimpContext.ImportFileFromStream(fileStream, AssimpPostProcessSteps, formatHint);

                LogMessage("Import complete");
            }
            catch (Exception ex)
            {
                LogMessage("Error importing: " + ex.Message);
                throw;
            }

            return assimpScene;
        }

        /// <summary>
        /// ReadFileToAssimpScene reads the specifed file and returns Assimp's Scene object.
        /// Assimp's Scene object can be manually converted into WPF's object with AssimpWpfConverter class.
        /// </summary>
        /// <param name="fileName">file name</param>
        /// <returns>Assimp's Scene object</returns>
        public Scene ReadFileToAssimpScene(string fileName)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("AssimpWpfImporter has been disposed");

            if (!_assimpContext.IsImportFormatSupported(Path.GetExtension(fileName)))
            {
                LogMessage(string.Format("File extension {0} is not supported by assimp.", Path.GetExtension(fileName)));
                return null;
            }

            LogMessage("Start importing " + fileName, "");


            var usedPostProcessSteps = AssimpPostProcessSteps;
            if (ReadPolygonIndices)
                usedPostProcessSteps = usedPostProcessSteps & ~PostProcessSteps.Triangulate; // When we are reading edge lines, we need to prevent Triangulate post process step

            Scene assimpScene = null;

            try
            {
                assimpScene = _assimpContext.ImportFile(fileName, usedPostProcessSteps);

                LogMessage("Import complete");
            }
            catch (Exception ex)
            {
                LogMessage("Error importing: " + ex.Message);
                throw;
            }

            return assimpScene;
        }

        /// <summary>
        /// Checks if the file extension (e.g. ".dae" or ".obj") is supported for import.
        /// </summary>
        /// <param name="fileExtension">file extension</param>
        /// <returns>
        /// True if the file extension is supported, false otherwise
        /// </returns>
        public bool IsImportFormatSupported(string fileExtension)
        {
            if (IsDisposed)
                throw new ObjectDisposedException("AssimpWpfImporter has been disposed");

            return _assimpContext.IsImportFormatSupported(fileExtension);
        }


        /// <summary>
        /// Loads assimp native library from the specified folder. If folder is not specified than the folder when the managed Assimp wrapper library (AssimpNet.dll) is located is used.
        /// </summary>
        /// <param name="assimpLibraryFolder">folder when the native assimp libraries are located. If folder is not specified than the folder when the managed Assimp wrapper library (AssimpNet.dll) is located is used.</param>
        public static void LoadAssimpNativeLibrary(string assimpLibraryFolder = null)
        {
            LoadAssimpNativeLibrary(assimpLibraryFolder, assimpLibraryFolder);
        }

        /// <summary>
        /// Loads assimp native library from the specified folder or full file path. If folder is not specified than the folder when the managed Assimp wrapper library (AssimpNet.dll) is located is used.
        /// </summary>
        /// <param name="assimp32BitLibrary">folder or full file path of the 32 bit native assimp library.</param>
        /// <param name="assimp64BitLibrary">folder or full file path of the 64 bit native assimp library.</param>
        public static void LoadAssimpNativeLibrary(string assimp32BitLibrary, string assimp64BitLibrary)
        {
            if (AssimpLibrary.Instance.IsLibraryLoaded)
                return;

            string libraryFilePath = null;
            
            if (Environment.Is64BitProcess)
            {
                // x64
                if (assimp64BitLibrary == null)
                    assimp64BitLibrary = Path.GetDirectoryName(typeof(AssimpLibrary).Assembly.Location);

                if (!string.IsNullOrEmpty(assimp64BitLibrary))
                {
                    if (assimp64BitLibrary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        libraryFilePath = assimp64BitLibrary;
                    else
                        libraryFilePath = Path.Combine(assimp64BitLibrary, Assimp64FileName);
                }
            }
            else
            {
                // x86
                if (assimp32BitLibrary == null)
                    assimp32BitLibrary = Path.GetDirectoryName(typeof(AssimpLibrary).Assembly.Location);

                if (!string.IsNullOrEmpty(assimp32BitLibrary))
                {
                    if (assimp32BitLibrary.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        libraryFilePath = assimp32BitLibrary;
                    else
                        libraryFilePath = Path.Combine(assimp32BitLibrary, Assimp32FileName);
                }
            }

            if (string.IsNullOrEmpty(libraryFilePath) || !System.IO.File.Exists(libraryFilePath))
                throw new Exception("Cannot find native assimp library. Please call AssimpWpfImporter.LoadAssimpNativeLibrary and provide path to the library.");

            AssimpLibrary.Instance.LoadLibrary(libraryFilePath);
        }

        private void LogMessage(string msg, string data = "")
        {
            if (_loggerCallback == null)
                return;

            _loggerCallback(msg, data);
        }

        /// <summary>
        /// Disposes all unmanaged and managed resources. After calling Dispose, user cannot call any methods on this instace of AssimpWpfImporter.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed)
                return;

            if (ImportedAssimpScene != null)
            {
                ImportedAssimpScene.Clear();
                ImportedAssimpScene = null;
            }

            if (_assimpContext != null)
            {
                _assimpContext.Dispose();
                _assimpContext = null;
            }

            NamedObjects = null;
            _assimpWpfConverter = null;

            LoggerCallback = null; // Logger callback can hold reference to this AssimpWpfImporter in its static delegate

            IsDisposed = true;
        }
    }
}