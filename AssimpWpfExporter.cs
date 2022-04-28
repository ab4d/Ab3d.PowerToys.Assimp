using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Assimp;
using Material = Assimp.Material;
using Vector3D = Assimp.Vector3D;

// This sample shows how to export WPF 3D scene or individual object to a file that is supported by assimp (see below for a list)
// The sample is using AssimpWpfExporter that internally uses native assimp library.
//
// Exporting 3D lines:
// 3D lines in Ab3d.PowerToys are created with triangles that are updated to always face the camera.
// When the 3D scene with 3D lines is exported, the current MeshGeometry3D objects of the 3D lines is also exported.
// But this means that because after exporting the MeshGeometry3D for the 3D line is fixed, it will not look good under some other camera angle.
// Therefore it is recommended to use TubeLineVisual3D or TubePathVisual3D objects instead of 3D lines. Those two objects are correctly exported to files.


// The following export formats are supported with the current version of assimp.
// Note that some file formats are better implemented than other:

// FormatId | Extension | Description
// collada  | .dae      | COLLADA - Digital Asset Exchange Schema
// x        | .x        | X Files
// stp      | .stp      | Step Files
// obj      | .obj      | Wavefront OBJ format
// objnomtl | .obj      | Wavefront OBJ format without material file
// stl      | .stl      | Stereolithography
// stlb     | .stl      | Stereolithography (binary)
// ply      | .ply      | Stanford Polygon Library
// plyb     | .ply      | Stanford Polygon Library (binary)
// 3ds      | .3ds      | Autodesk 3DS (legacy)
// gltf     | .gltf     | GL Transmission Format
// glb      | .glb      | GL Transmission Format (binary)
// gltf2    | .gltf     | GL Transmission Format v. 2
// glb2     | .glb      | GL Transmission Format v. 2 (binary)
// assbin   | .assbin   | Assimp Binary
// assxml   | .assxml   | Assxml Document
// x3d      | .x3d      | Extensible 3D
// fbx      | .fbx      | Autodesk FBX (binary)
// fbxa     | .fbx      | Autodesk FBX (ascii)
// 3mf      | .3mf      | The 3MF-File-Format 

// List of file formats was get with the following line:
//var assimpWpfExporter = new AssimpWpfExporter();
//_exportFormatDescriptions = assimpWpfExporter.ExportFormatDescriptions;
//System.Diagnostics.Debug.WriteLine(string.Join("\r\n", _exportFormatDescriptions.Select(f => f.FormatId.PadRight(8) + " | ." + f.FileExtension.PadRight(8) + " | " + f.Description).ToArray()));


namespace Ab3d.Assimp
{
    /// <summary>
    /// AssimpWpfExporter class can be used to export the WPF 3D scene to many different file formats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AssimpWpfExporter</b> class can be used to export the WPF 3D scene to many different file formats.
    /// </para>
    /// <para>
    /// The AssimpWpfExporter uses an open source assimp library to do the export. 
    /// Therefore in the current step the WPF 3D objects are converted into Assimp Scene object. 
    /// Then the export method on assimp scene is called.
    /// </para>
    /// <para>
    /// To get a list of supported file formats check the <see cref="ExportFormatDescriptions"/>.
    /// </para>
    /// </remarks>
    public class AssimpWpfExporter : IDisposable
    {
        private static ExportFormatDescription[] _exportFormatDescriptions;

        /// <summary>
        /// Gets an array of ExportFormatDescription structs that define possible export file formats.
        /// </summary>
        public ExportFormatDescription[] ExportFormatDescriptions
        {
            get
            {
                return EnsureExportFormatDescriptions();
            }
        }

        private ExportFormatDescription[] EnsureExportFormatDescriptions()
        {
            var exportFormatDescriptions = _exportFormatDescriptions;

            if (exportFormatDescriptions == null)
            {
                var assimpContext = new AssimpContext();

                exportFormatDescriptions  = assimpContext.GetSupportedExportFormats();
                _exportFormatDescriptions = exportFormatDescriptions;

                assimpContext.Dispose();
            }

            return exportFormatDescriptions;
        }


        private Action<string, string> _loggerCallback;

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
        /// Gets or sets a Dictionary where keys are objects and values are object names.
        /// </summary>
        public Dictionary<object, string> ObjectNames { get; set; }

        /// <summary>
        /// Gets or sets a Dictionary where keys are object names and values are objects.
        /// </summary>
        public Dictionary<string, object> NamedObjects { get; set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if full texture file path is exported when writing textures.
        /// If false (by default), then only file name is exported (and a custom texture path that can be specified by <see cref="ExportedTexturePath"/>.
        /// </summary>
        [Obsolete("ExportFullTexturePaths is obsolete. Please use ExportTextureCallback instead.")]
        public bool ExportFullTexturePaths { get; set; }

        /// <summary>
        /// Gets or sets a string that specifies the custom textures paths that is added to the texture file name. Default value is null (no textures path).
        /// This property is used only when <see cref="ExportFullTexturePaths"/> is false.
        /// </summary>
        [Obsolete("ExportedTexturePath is obsolete. Please use ExportTextureCallback instead.")]
        public string ExportedTexturePath { get; set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if textures are embedded into the exported file (only when the exported file support that). False by default.
        /// When this property is true, then the <see cref="ExportTextureCallback"/> is not called.
        /// Note that not all file types support embedded textures. If this is not supported and if IsEmbeddingTextures is true, then the textures will not be exported. 
        /// </summary>
        public bool IsEmbeddingTextures { get; set; }

        /// <summary>
        /// Delegate that is used to export the specified bitmap as a texture. The delegate should return file name and relative path that is written to the exported file.
        /// If null is returned, then the bitmap is not exported.
        /// </summary>
        /// <param name="bitmapSource">BitmapSource</param>
        /// <returns>file name and path that is written to the exported file</returns>
        public delegate string ExportTextureDelegate(BitmapSource bitmapSource);

        /// <summary>
        /// ExportTextureCallback is called for each texture. User should set this delegate to the method that saves the bitmap and then returns the relative path to the saved bitmap.
        /// If null is returned, then the bitmap is not exported.
        /// </summary>
        public ExportTextureDelegate ExportTextureCallback;

        /// <summary>
        /// Gets an Assimp Scene object that is generated from the WPF 3D objects
        /// </summary>
        public Scene AssimpScene { get; private set; }


        private Dictionary<System.Windows.Media.Media3D.Material, int> _wpf2AssimpMaterials;

        private static FieldInfo _meshTextureCoordinatesFieldInfo;
        private static FieldInfo _meshTexComponentCountFieldInfo;

        private static PropertyInfo _visual3DModelPropertyInfo;


        /// <summary>
        /// Initializes a new instance of the AssimpWpfExporter class.
        /// </summary>
        public AssimpWpfExporter()
        {
            _wpf2AssimpMaterials = new Dictionary<System.Windows.Media.Media3D.Material, int>();

            if (AssimpScene != null)
                AssimpScene.Clear();

            AssimpScene = new Scene();

            // NOTE:
            // In previous versions (9.3 and before), this code set SceneFlags to NonVerboseFormat (as for the comment below).
            // But it was found out that this can break exporting to stl files (see https://forum.ab4d.com/showthread.php?tid=4226&pid=5632#pid5632 ("merge stl files").
            // Therefore this code was commented.
            //
            // Set NonVerboseFormat flag - this is based on the comment from Exporter.cpp comment:
            // when they create scenes from scratch, users will likely create them not in verbose
            // format. They will likely not be aware that there is a flag in the scene to indicate
            // this, however. To avoid surprises and bug reports, we check for duplicates in
            // meshes upfront.
            //AssimpScene.SceneFlags |= SceneFlags.NonVerboseFormat;
        }

        /// <summary>
        /// Exports the 3D objects that were added to this AssimpWpfExporter to the specified file name and specific file format.
        /// </summary>
        /// <param name="fileName">output file name</param>
        /// <param name="exportFormatId">formatId of the exported file (see FormatId from <see cref="ExportFormatDescriptions"/> for more information)</param>
        /// <returns>true if file was successfully exported</returns>
        public bool Export(string fileName, string exportFormatId)
        {
            return ExportAssimpScene(AssimpScene, fileName, exportFormatId);
        }

        /// <summary>
        /// Exports the 3D objects that were added to this AssimpWpfExporter into a ExportDataBlob (using the specific file format).
        /// </summary>
        /// <param name="exportFormatId">formatId of the exported file (see FormatId from <see cref="ExportFormatDescriptions"/> for more information)</param>
        /// <returns>ExportDataBlob</returns>
        public ExportDataBlob ExportToDataBlob(string exportFormatId)
        {
            return ExportAssimpSceneToDataBlob(AssimpScene, exportFormatId);
        }

        /// <summary>
        /// ExportViewport3D exports the 3D objects from the viewport3D to the specified file name and specific file format.
        /// </summary>
        /// <param name="viewport3D">Viewport3D's Children will be added to this AssimpWpfExporter</param>
        /// <param name="fileName">output file name</param>
        /// <param name="exportFormatId">formatId of the exported file (see FormatId from <see cref="ExportFormatDescriptions"/> for more information)</param>
        /// <returns>true if file was successfully exported</returns>
        public static bool ExportViewport3D(Viewport3D viewport3D, string fileName, string exportFormatId)
        {
            var assimpWpfExporter = new AssimpWpfExporter();
            assimpWpfExporter.AddViewport3D(viewport3D);

            bool success = assimpWpfExporter.Export(fileName, exportFormatId);

            return success;
        }

        /// <summary>
        /// ExportAssimpScene exports the Assimp scene to the specified file name and specific file format.
        /// </summary>
        /// <param name="assimpScene">Assimp scene</param>
        /// <param name="fileName">output file name</param>
        /// <param name="exportFormatId">formatId of the exported file (see FormatId from <see cref="ExportFormatDescriptions"/> for more information)</param>
        /// <returns>true if file was successfully exported</returns>
        public static bool ExportAssimpScene(Scene assimpScene, string fileName, string exportFormatId)
        {
            var assimpContext = new AssimpContext();
            bool success = assimpContext.ExportFile(assimpScene, fileName, exportFormatId);

            return success;
        }

        /// <summary>
        /// ExportAssimpScene exports the Assimp scene into a ExportDataBlob (using the specific file format).
        /// </summary>
        /// <param name="assimpScene">Assimp scene</param>
        /// <param name="exportFormatId">formatId of the exported file (see FormatId from <see cref="ExportFormatDescriptions"/> for more information)</param>
        /// <returns>ExportDataBlob</returns>
        public static ExportDataBlob ExportAssimpSceneToDataBlob(Scene assimpScene, string exportFormatId)
        {
            var assimpContext = new AssimpContext();
            var dataBlob = assimpContext.ExportToBlob(assimpScene, exportFormatId);

            return dataBlob;
        }

        /// <summary>
        /// AddViewport3D adds 3D objects from the viewport3D to this AssimpWpfExporter.
        /// </summary>
        /// <param name="viewport3D">Viewport3D's Children will be added to this AssimpWpfExporter</param>
        public void AddViewport3D(Viewport3D viewport3D)
        {
            EnsureObjectNames();

            var rootNode = new Node();

            foreach (var visual3D in viewport3D.Children)
            {
                Node assimpNode = ConvertVisual3D(visual3D);
                if (assimpNode != null)
                    rootNode.Children.Add(assimpNode);
            }

            AddRootNode(rootNode);
        }

        /// <summary>
        /// AddVisual3D adds 3D objects from the WPF's Visual3D to this AssimpWpfExporter.
        /// </summary>
        /// <param name="visual3D">WPF's Visual3D</param>
        public void AddVisual3D(System.Windows.Media.Media3D.Visual3D visual3D)
        {
            EnsureObjectNames();

            Node assimpNode = ConvertVisual3D(visual3D);

            AddRootNode(assimpNode);
        }

        /// <summary>
        /// AddModel adds 3D objects from the WPF's Model3D to this AssimpWpfExporter.
        /// </summary>
        /// <param name="wpfModel">WPF's Model3D</param>
        public void AddModel(System.Windows.Media.Media3D.Model3D wpfModel)
        {
            EnsureObjectNames();

            Node assimpNode = ConvertModel(wpfModel);

            AddRootNode(assimpNode);
        }

        private void AddRootNode(Node assimpNode)
        {
            if (assimpNode == null)
                return;

            if (AssimpScene.RootNode == null)
            {
                AssimpScene.RootNode = assimpNode;
            }
            else
            {
                var savedRootNode = AssimpScene.RootNode;

                var newRootNode = new Node();
                newRootNode.Children.Add(savedRootNode);
                newRootNode.Children.Add(assimpNode);

                AssimpScene.RootNode = newRootNode;
            }
        }

        private void EnsureObjectNames()
        {
            if (ObjectNames == null && NamedObjects != null)
            {
                ObjectNames = new Dictionary<object, string>(NamedObjects.Count);

                foreach (KeyValuePair<string, object> keyValuePair in NamedObjects)
                    ObjectNames[keyValuePair.Value] = keyValuePair.Key;
            }
        }

        // Exporting cameras is not supported in assimp - this does not work for Collada - other file formats do not support that either
        //private void SetAssimpCamera(Scene assimpScene, System.Windows.Media.Media3D.Camera wpfCamera, float aspectRadio)
        //{
        //    var perspectiveCamera = wpfCamera as System.Windows.Media.Media3D.PerspectiveCamera;

        //    if (perspectiveCamera == null)
        //        return;

        //    var camera = new Camera("WpfCamera", perspectiveCamera.Position.ToAssimpVector3D(), perspectiveCamera.UpDirection.ToAssimpVector3D(), perspectiveCamera.LookDirection.ToAssimpVector3D(), (float) perspectiveCamera.FieldOfView, (float) perspectiveCamera.NearPlaneDistance, (float) perspectiveCamera.FarPlaneDistance, aspectRadio);

        //    assimpScene.Cameras.Add(camera);
        //}


        private Node ConvertVisual3D(System.Windows.Media.Media3D.Visual3D visual3D)
        {
            string name = GetObjectName(visual3D);

            Node assimpNode = new Node(name);

            var modelVisual3D = visual3D as ModelVisual3D;
            if (modelVisual3D != null)
            {
                if (modelVisual3D.Content != null)
                {
                    var contentAssimpNode = ConvertModel(modelVisual3D.Content);
                    if (contentAssimpNode != null)
                        assimpNode.Children.Add(contentAssimpNode);
                }

                if (modelVisual3D.Children.Count > 0)
                {
                    foreach (var child in modelVisual3D.Children)
                    {
                        var childAssimpNode = ConvertVisual3D(child);
                        assimpNode.Children.Add(childAssimpNode);
                    }
                }
            }
            else
            {
                var uiElement3D = visual3D as UIElement3D;

                if (uiElement3D != null)
                {
                    var containerUiElement3D = uiElement3D as ContainerUIElement3D;

                    if (containerUiElement3D != null)
                    {
                        foreach (var child in containerUiElement3D.Children)
                        {
                            var childAssimpNode = ConvertVisual3D(child);
                            assimpNode.Children.Add(childAssimpNode);
                        }
                    }
                    else
                    {
                        if (_visual3DModelPropertyInfo == null)
                            _visual3DModelPropertyInfo = typeof (Visual3D).GetProperty("Visual3DModel", BindingFlags.Instance | BindingFlags.Public);

                        var uiElementModel = _visual3DModelPropertyInfo.GetValue(uiElement3D, null) as Model3D;

                        if (uiElementModel != null)
                        {
                            var contentAssimpNode = ConvertModel(uiElementModel);
                            if (contentAssimpNode != null)
                                assimpNode.Children.Add(contentAssimpNode);
                        }
                    }
                }
                else
                {
                    assimpNode = null; // Do not return empty node - return null instead
                }
            }

            SetTransform(assimpNode, visual3D.Transform);

            return assimpNode;
        }

        private Node ConvertModel(System.Windows.Media.Media3D.Model3D wpfModel)
        {
            string name = GetObjectName(wpfModel);

            Node assimpNode = new Node(name);

            if (wpfModel is Model3DGroup)
            {
                foreach (var model3D in ((Model3DGroup) wpfModel).Children)
                {
                    Node newNode = ConvertModel(model3D);
                    assimpNode.Children.Add(newNode);
                }
            }
            else if (wpfModel is GeometryModel3D)
            {
                var geometryModel3D = (GeometryModel3D) wpfModel;

                var meshGeometry3D = geometryModel3D.Geometry as MeshGeometry3D;

                if (meshGeometry3D != null)
                {
                    Mesh assimpMesh = ConvertMeshGeometry3D(meshGeometry3D);

                    if (geometryModel3D.Material != null)
                    {
                        // NOTE:
                        // Two sided materials are not supported
                        // It is possible to set the IsTwoSided to true, but this is not exported
                        bool isTwoSidedMaterial = ReferenceEquals(geometryModel3D.Material, geometryModel3D.BackMaterial);

                        int materialIndex = GetAssimpMaterialIndex(geometryModel3D.Material, isTwoSidedMaterial);
                        assimpMesh.MaterialIndex = materialIndex;
                    }

                    var allAssimpMeshes = AssimpScene.Meshes;

                    int meshIndex = allAssimpMeshes.Count;

                    allAssimpMeshes.Add(assimpMesh);
                    assimpNode.Meshes.Add(meshIndex);
                }
            }
            else
            {
                assimpNode = null; // Do not return empty node - return null instead
            }
            // Lights are not supported by Assimp exporter

            SetTransform(assimpNode, wpfModel.Transform);

            return assimpNode;
        }

        private string GetObjectName(DependencyObject dependencyObject)
        {
            string name;

            if (ObjectNames == null || !ObjectNames.TryGetValue(dependencyObject, out name))
                name = GetName(dependencyObject); // Try to get value Name DependencyProperty (used by Ab3d.PowerToys)

            if (name == null)
                name = ""; // Default constructor without name sets name to empty string

            return name;
        }

        private void SetTransform(Node assimpNode, Transform3D transform3D)
        {
            if (assimpNode == null || transform3D == null)
                return;

            var assimpMatrix = transform3D.Value;

            if (!assimpMatrix.IsIdentity)
            {
                // Assimp saves the matrix in row major so we need to flip the matrix data
                assimpNode.Transform = new Matrix4x4((float)assimpMatrix.M11, (float)assimpMatrix.M21, (float)assimpMatrix.M31, (float)assimpMatrix.OffsetX,
                                                     (float)assimpMatrix.M12, (float)assimpMatrix.M22, (float)assimpMatrix.M32, (float)assimpMatrix.OffsetY,
                                                     (float)assimpMatrix.M13, (float)assimpMatrix.M23, (float)assimpMatrix.M33, (float)assimpMatrix.OffsetZ,
                                                     (float)assimpMatrix.M14, (float)assimpMatrix.M24, (float)assimpMatrix.M34, (float)assimpMatrix.M44);
            }
        }

        private int GetAssimpMaterialIndex(System.Windows.Media.Media3D.Material wpfMaterial, bool isTwoSidedMaterial)
        {
            int materialIndex;

            if (!_wpf2AssimpMaterials.TryGetValue(wpfMaterial, out materialIndex))
            {
                List<Material> allAssimpMaterials = AssimpScene.Materials;
                int materialsCount = allAssimpMaterials.Count;


                var assimpMaterial = new Material();

                string materialName;

                if (ObjectNames == null || !ObjectNames.TryGetValue(wpfMaterial, out materialName))
                {
                    materialName = GetName(wpfMaterial); // Try to get value Name depandency property (used by Ab3d.PowerToys)

                    if (string.IsNullOrEmpty(materialName))
                        materialName = "Material" + (materialsCount + 1).ToString();
                }

                assimpMaterial.Name = materialName;

                assimpMaterial.ShadingMode = ShadingMode.Phong;
                assimpMaterial.ColorDiffuse = new Color4D(1, 1, 1, 1);
                assimpMaterial.ColorAmbient = new Color4D(0, 0, 0, 1);
                assimpMaterial.ColorEmissive = new Color4D(0, 0, 0, 1);
                assimpMaterial.ColorSpecular = new Color4D(0, 0, 0, 1);
                assimpMaterial.Shininess = 16.0f;
                assimpMaterial.ShininessStrength = 1.0f;

                AddMaterialProperties(wpfMaterial, assimpMaterial);


                // Setting IsTwoSided material does not change the dae file
                if (isTwoSidedMaterial)
                    assimpMaterial.IsTwoSided = true;

                //MaterialProperty twoSidedMaterialProperty = assimpMaterial.GetProperty("$mat.twosided,0,0"); // Assimp.Unmanaged.AiMatKeys.TWOSIDED = "$mat.twosided,0,0";
                //if (twoSidedMaterialProperty == null)
                //{
                //    twoSidedMaterialProperty = new MaterialProperty("$mat.twosided", true); // Assimp.Unmanaged.AiMatKeys.TWOSIDED_BASE = "$mat.twosided"
                //    assimpMaterial.AddProperty(twoSidedMaterialProperty);
                //}

                //twoSidedMaterialProperty.SetBooleanValue(true);


                allAssimpMaterials.Add(assimpMaterial);
                _wpf2AssimpMaterials.Add(wpfMaterial, materialsCount);

                materialIndex = materialsCount;
            }

            return materialIndex;
        }

        private void AddMaterialProperties(System.Windows.Media.Media3D.Material wpfMaterial, Material assimpMaterial)
        {
            if (wpfMaterial is System.Windows.Media.Media3D.DiffuseMaterial)
            {
                AddDiffuseMaterialProperties((System.Windows.Media.Media3D.DiffuseMaterial) wpfMaterial, assimpMaterial);
            }
            else if (wpfMaterial is System.Windows.Media.Media3D.SpecularMaterial)
            {
                AddSpecularMaterialProperties((System.Windows.Media.Media3D.SpecularMaterial) wpfMaterial, assimpMaterial);
            }
            else if (wpfMaterial is System.Windows.Media.Media3D.EmissiveMaterial)
            {
                AddEmissiveMaterialProperties((System.Windows.Media.Media3D.EmissiveMaterial) wpfMaterial, assimpMaterial);
            }
            else if (wpfMaterial is System.Windows.Media.Media3D.MaterialGroup)
            {
                var group = (System.Windows.Media.Media3D.MaterialGroup) wpfMaterial;

                foreach (var material in group.Children)
                    AddMaterialProperties(material, assimpMaterial);
            }
        }

        private void AddDiffuseMaterialProperties(System.Windows.Media.Media3D.DiffuseMaterial wpfMaterial, Material assimpMaterial)
        {
            var solidColorBrush = wpfMaterial.Brush as SolidColorBrush;
            if (solidColorBrush != null)
            {
                assimpMaterial.ColorDiffuse = solidColorBrush.Color.ToAssimpColor();
            }
            else
            {
                var imageBrush = wpfMaterial.Brush as ImageBrush;
                if (imageBrush != null)
                {
                    var bitmapSource = imageBrush.ImageSource as BitmapSource;
                    if (bitmapSource != null)
                    {
                        TextureWrapMode xWrap, yWrap;

                        switch (imageBrush.TileMode)
                        {
                            case TileMode.None:
                                xWrap = TextureWrapMode.Clamp;
                                yWrap = TextureWrapMode.Clamp;
                                break;

                            case TileMode.Tile:
                                xWrap = TextureWrapMode.Wrap;
                                yWrap = TextureWrapMode.Wrap;
                                break;

                            case TileMode.FlipX:
                                xWrap = TextureWrapMode.Mirror;
                                yWrap = TextureWrapMode.Clamp;
                                break;

                            case TileMode.FlipY:
                                xWrap = TextureWrapMode.Clamp;
                                yWrap = TextureWrapMode.Mirror;
                                break;

                            case TileMode.FlipXY:
                                xWrap = TextureWrapMode.Mirror;
                                yWrap = TextureWrapMode.Mirror;
                                break;

                            default:
                                xWrap = TextureWrapMode.Clamp;
                                yWrap = TextureWrapMode.Clamp;
                                break;
                        }


                        var bitmapImage = bitmapSource as BitmapImage;

                        string fileName;
                        if (bitmapImage != null && bitmapImage.UriSource != null)
                            fileName = bitmapImage.UriSource.OriginalString;
                        else
                            fileName = null;


                        string exportedFileName;

                        if (IsEmbeddingTextures)
                        {
                            if (fileName == null)
                                exportedFileName = string.Format("*{0}", AssimpScene.Textures.Count); // When no file name is available save the name as index of the embedded texture
                            else
                                exportedFileName = System.IO.Path.GetFileName(fileName); // Use only file name without path when using embedded texture

                            AddEmbeddedTexture(exportedFileName, bitmapSource);
                        }
                        else if (ExportTextureCallback != null)
                        {
                            exportedFileName = ExportTextureCallback(bitmapSource);
                        }
                        else
                        {
                            exportedFileName = GetFileName(fileName);
                        }

                        if (exportedFileName != null)
                            assimpMaterial.TextureDiffuse = new TextureSlot(exportedFileName, TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Add, xWrap, yWrap, 0);
                    }
                }
            }
        }

        private void AddEmbeddedTexture(string fileName, BitmapSource bitmapSource)
        {
            if (AssimpScene.Textures == null)
                return;


            string fullFileName;

            var bitmapImage = bitmapSource as BitmapImage;

            if (bitmapImage != null && bitmapImage.UriSource != null)
            {
                if (bitmapImage.UriSource.IsAbsoluteUri)
                    fullFileName = bitmapImage.UriSource.LocalPath; // LocalPath is supported only when using AbsoluteUri
                else
                    fullFileName = System.IO.Path.Combine(Environment.CurrentDirectory, bitmapImage.UriSource.OriginalString);
            }
            else
            {
                fullFileName = null;
            }


            byte[] fileBytes;
            string fileExtension;

            if (fullFileName != null && System.IO.File.Exists(fullFileName))
            {
                // Read the original file and embed its content

                fileBytes = System.IO.File.ReadAllBytes(fullFileName);

                fileExtension = System.IO.Path.GetExtension(fileName);
                if (fileExtension.StartsWith("."))
                    fileExtension = fileExtension.Substring(1);
            }
            else
            {
                using (var memoryStream = new MemoryStream())
                {
                    PngBitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmapSource));
                    enc.Save(memoryStream);

                    fileBytes = memoryStream.ToArray();
                    fileExtension = "png";
                }
            }

            // This requires AssimpNet v5.1.0.2
            var embeddedTexture = new EmbeddedTexture(fileName, fileExtension, fileBytes);

            // With older AssimpNet v5.1.0.0 use reflection to set FileName:
            //var fileNameProperty = typeof(EmbeddedTexture).GetProperty("FileName", BindingFlags.Instance | BindingFlags.Public);
            //fileNameProperty.SetValue(embeddedTexture, System.IO.Path.GetFileName(fileName));
            
            AssimpScene.Textures.Add(embeddedTexture);
        }

        private string GetFileName(string fileName)
        {
            if (fileName == null)
                return null;

#pragma warning disable CS0618
            if (ExportFullTexturePaths)
            {
                if (!System.IO.Path.IsPathRooted(fileName))
                    fileName = System.IO.Path.Combine(Environment.CurrentDirectory, fileName);

                return fileName;
            }

            string exportedFileName = System.IO.Path.GetFileName(fileName);

            if (!string.IsNullOrEmpty(ExportedTexturePath))
                exportedFileName = System.IO.Path.Combine(ExportedTexturePath, exportedFileName);
#pragma warning restore CS0618

            return exportedFileName;
        }

        private static void AddSpecularMaterialProperties(System.Windows.Media.Media3D.SpecularMaterial wpfMaterial, Material assimpMaterial)
        {
            if (wpfMaterial.Brush is SolidColorBrush)
                assimpMaterial.ColorSpecular = ((SolidColorBrush) wpfMaterial.Brush).Color.ToAssimpColor();

            assimpMaterial.Shininess = (float) wpfMaterial.SpecularPower;
            assimpMaterial.ShininessStrength = 1.0f;
        }

        private static void AddEmissiveMaterialProperties(System.Windows.Media.Media3D.EmissiveMaterial wpfMaterial, Material assimpMaterial)
        {
            if (wpfMaterial.Brush is SolidColorBrush)
                assimpMaterial.ColorEmissive = ((SolidColorBrush) wpfMaterial.Brush).Color.ToAssimpColor();
        }

        private static string GetName(DependencyObject dependencyObject)
        {
            return dependencyObject.GetValue(FrameworkElement.NameProperty) as string;
        }

        //private static string GetName(Model3D model3D)
        //{
        //    return model3D.GetValue(FrameworkElement.NameProperty) as string;
        //}

        //private static string GetName(Visual3D visual3D)
        //{
        //    return visual3D.GetValue(FrameworkElement.NameProperty) as string;
        //}

        //private static string GetName(System.Windows.Media.Media3D.Material material)
        //{
        //    return material.GetValue(FrameworkElement.NameProperty) as string;
        //}

        private Mesh ConvertMeshGeometry3D(MeshGeometry3D wpfMesh)
        {
            if (wpfMesh == null)
                return null;

            var assimpMesh = new Mesh(PrimitiveType.Triangle);

            Point3DCollection positions = wpfMesh.Positions;

            if (positions != null && positions.Count > 0)
            {
                int positionsCount = positions.Count;
                var assimpVectors = new Vector3D[positionsCount];

                for (var i = 0; i < positionsCount; i++)
                    assimpVectors[i] = new Vector3D((float) positions[i].X, (float) positions[i].Y, (float) positions[i].Z);

                assimpMesh.Vertices.AddRange(assimpVectors);
            }

            Int32Collection triangleIndices = wpfMesh.TriangleIndices;
            if (triangleIndices != null && triangleIndices.Count > 0)
            {
                assimpMesh.SetIndices(triangleIndices.ToArray(), 3);
            }

            if (wpfMesh.TextureCoordinates != null && wpfMesh.TextureCoordinates.Count > 0)
            {
                var assimpTextureCoordinatesArray = new List<Vector3D>[1];

                // UH: We need to use reflection to add texture coordinates
                if (_meshTextureCoordinatesFieldInfo == null)
                    _meshTextureCoordinatesFieldInfo = typeof (Mesh).GetField("m_texCoords", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_meshTexComponentCountFieldInfo == null)
                    _meshTexComponentCountFieldInfo = typeof (Mesh).GetField("m_texComponentCount", BindingFlags.Instance | BindingFlags.NonPublic);


                if (_meshTextureCoordinatesFieldInfo != null)
                {
                    var wpfTextureCoordinates = wpfMesh.TextureCoordinates;
                    int textureCoordinatesCount = wpfTextureCoordinates.Count;

                    var assimpTextureCoordinatesList = new List<Vector3D>(textureCoordinatesCount);
                    assimpTextureCoordinatesArray[0] = assimpTextureCoordinatesList;

                    for (var i = 0; i < textureCoordinatesCount; i++)
                        assimpTextureCoordinatesList.Add(new Vector3D((float) wpfTextureCoordinates[i].X, 1.0f - (float) wpfTextureCoordinates[i].Y, 0));

                    _meshTextureCoordinatesFieldInfo.SetValue(assimpMesh, assimpTextureCoordinatesArray);

                    if (_meshTexComponentCountFieldInfo != null)
                    {
                        var texComponentCount = new int[8];
                        texComponentCount[0] = 2;

                        _meshTexComponentCountFieldInfo.SetValue(assimpMesh, texComponentCount);
                    }
                }
                else
                {
                    LogMessage("WARNING: Cannot get internal texture coordinates field");
                }
            }

            if (wpfMesh.Normals != null && wpfMesh.Normals.Count > 0)
            {
                var wpfNormals = wpfMesh.Normals;
                int normalsCount = wpfNormals.Count;
                var assimpNormals = new Vector3D[normalsCount];

                for (var i = 0; i < normalsCount; i++)
                    assimpNormals[i] = new Vector3D((float) wpfNormals[i].X, (float) wpfNormals[i].Y, (float) wpfNormals[i].Z);

                assimpMesh.Normals.AddRange(assimpNormals);
            }

            return assimpMesh;
        }

        private void LogMessage(string msg, string data = "")
        {
            if (LoggerCallback == null)
                return;

            LoggerCallback(msg, data);
        }

        /// <summary>
        /// Disposes the unmanaged assimp resources
        /// </summary>
        public void Dispose()
        {
            _wpf2AssimpMaterials = null;

            if (AssimpScene != null)
            {
                AssimpScene.Clear();
                AssimpScene = null;
            }
        }
    }
}