using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Assimp;
using Material = Assimp.Material;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace Ab3d.Assimp
{
    // Assimp data structures documentation:
    // http://www.assimp.org/lib_html/data.html
    //
    // - Always uses right handed coordinate systme
    // - output face winding is counter clockwise
    // - output UV coordinate system has its origin in the lower-left corner
    // - all matrices in the library are row-major

    /// <summary>
    /// AssimpWpfConverter can convert Assimp Scene object to WPF Model3D object.
    /// </summary>
    public class AssimpWpfConverter
    {
        private const string ASSIMP_DEFAULT_MATERIAL_NAME = "DefaultMaterial";

        private const string LOGGER_PREFIX = "AssimpWpfConverter: ";
        private const string SUPPORTED_IMAGE_FORMATS = ".jpg;.gif;.png;.bmp;.tif;.tiff;.jpeg"; // List of image file formats supported by the WPF

        private static readonly Color4D _blackColor4D = new Color4D(0, 0, 0, 1);

        private System.Windows.Media.Media3D.GeometryModel3D[] _wpfModels;

        private Scene _assimpScene;
        private string _texturesPath;
        private Func<string, Stream> _resolveResourceFunc;

        private bool _isAlreadyTriangulated;

        private static bool _isPolygonIndicesPropertyCreated;
        private static DependencyProperty _polygonIndicesProperty;

        //private bool _convertToRightHandedCoordinateSystem;

        private Dictionary<string, BitmapSource> _cachedBitmaps;

        private Dictionary<Mesh, GeometryModel3D> _createdGeometryModel3Ds;

        /// <summary>
        /// Callback action that can be used to log Assimp and AssimpWpfConverter log messages
        /// </summary>
        public Action<string, string> LoggerCallback { get; set; }

        /// <summary>
        /// Gets a list of WPF Materials from Assimp Scene object.
        /// </summary>
        public System.Windows.Media.Media3D.Material[] WpfMaterials { get; private set; }

        /// <summary>
        /// Gets a Dictionary of objects as keys and names as values. Can be used to quickly get the object name.
        /// </summary>
        public Dictionary<object, string> ObjectNames { get; private set; }

        /// <summary>
        /// Gets or sets a material that is used as default material (when material is not specified in the model file)
        /// </summary>
        public System.Windows.Media.Media3D.Material DefaultMaterial { get; set; }

        /// <summary>
        /// Gets or sets a Booleand that specifies if we always do a convertion from left to right handed coordinate system.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>ForceConvertToRightHandedCoordinateSystem</b> gets or sets a Booleand that specifies if we always do a convertion from left to right handed coordinate system.
        /// </para>
        /// <para>
        /// WPF uses right handed coordinate system - the Z axis points away from the screen. DirectX uses left handed coordinate system - there the Z axis points into the screen.
        /// </para>
        /// </remarks>
        public bool ForceConvertToRightHandedCoordinateSystem { get; set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if PolygonIndicesProperty is set to the created MeshGeometry3D objects. 
        /// This property defines the indexes of positions that define mesh polygons.
        /// This property is used only when the assimp scene was not read with Triangulation post process and when the Ab3d.PowerToys library is referenced.
        /// </summary>
        public bool ReadPolygonIndices { get; set; }

        /// <summary>
        /// Gets or sets a Boolean that specifies if simple triangle fan triangulation is used instead of standard triangulation. 
        /// This property is used only when the 3D model is not triangulated by assimp importer (when Triangulate PostProcessSteps is not used).
        /// Default value is false.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>UseSimpleTriangulation</b> gets or sets a Boolean that specifies if simple triangle fan triangulation is used instead of standard triangulation.
        /// </para>
        /// <para>
        /// Simple triangle fan triangulation is much faster, but can correctly triangulate only convex polygons.
        /// Standard triangulation is much slower but can triangulate convex and concave polygons.
        /// </para>
        /// <para>
        /// This property is used only when the 3D model is not triangulated by assimp importer (when Triangulate PostProcessSteps is not used).
        /// </para>
        /// <para>
        /// By default standard triangulation is used but if you know that the read models use only convex polygons, you can speed up reading 3D models with setting UseSimpleTriangulation to true.
        /// </para>
        /// </remarks>
        public bool UseSimpleTriangulation { get; set; }

        /// <summary>
        /// TriangulatorFunc is a static property that can be set to a Func that takes list of 2D positions, triangulates them and returns a list of triangle indices (list of int values).
        /// When this property is not set, then the triangulator from Ab3d.PowerToys will be used (loaded by Reflection).
        /// </summary>
        public static Func<List<Point>, List<int>> TriangulatorFunc { get; set; }

        private static Func<List<Point>, List<int>> _powerToysTriangulator;

        private static Type _triangulatorType;
        private static MethodInfo _createTriangleIndicesMethodInfo;

        /// <summary>
        /// Gets or sets a BitmapCacheOption that is used when creating bitmaps from files. Default value is OnLoad that caches the image in memory at load time (this does not lock the image file name).
        /// </summary>
        public BitmapCacheOption BitmapCacheOption { get; set; }

        /// <summary>
        /// Gets or sets a Boolena that specifies if Model3DGroups without any child objects are removed from the imported scene. Default value is true. 
        /// </summary>
        public bool RemoveEmptyModel3DGroups { get; set; }


        /// <summary>
        /// Constructor
        /// </summary>
        public AssimpWpfConverter()
        {
            DefaultMaterial = new DiffuseMaterial(Brushes.Silver);
            BitmapCacheOption = BitmapCacheOption.OnLoad;
            RemoveEmptyModel3DGroups = true;
        }

        /// <summary>
        /// ConvertAssimpModel converts the assimpScene into WPF Model3D that is retuned.
        /// </summary>
        /// <param name="assimpScene">Assimp scene</param>
        /// <param name="texturesPath">path to the textures (optional)</param>
        /// <param name="resolveResourceFunc">function that can resolve resource names (textures and other related files)</param>
        /// <param name="assimpPostProcessSteps">post process steps that were used to create assimpScene</param>
        /// <returns>WPF Model3D</returns>
        public System.Windows.Media.Media3D.Model3D ConvertAssimpModel(Scene assimpScene, string texturesPath = null, Func<string, Stream> resolveResourceFunc = null, PostProcessSteps assimpPostProcessSteps = PostProcessSteps.None)
        {
            _assimpScene = assimpScene;
            _texturesPath = texturesPath;
            _resolveResourceFunc = resolveResourceFunc;

            _isAlreadyTriangulated = (assimpPostProcessSteps & PostProcessSteps.Triangulate) != 0;

            if (ReadPolygonIndices)
                EnsurePolygonIndicesProperty();

            // Assimp data structures documentation:
            // http://www.assimp.org/lib_html/data.html
            //
            // - Always uses right handed coordinate systme
            // - output face winding is counter clockwise
            // - output UV coordinate system has its origin in the lower-left corner
            // - all matrices in the library are row-major

            // Assimp can also do the conversion from left to right handed coordinate system
            // This is done by adding a _leftToRightMatrix matrix to the root model
            // If we already convert the data to right handed coordinate system (adjusting each position and matrix), than we need to remove the _leftToRightMatrix transformation
            //if (ForceConvertToRightHandedCoordinateSystem || (assimpScene.RootNode != null && assimpScene.RootNode.Transform == _leftToRightMatrix))
            //{
            //    // Instead of using root transformation we will rather read the model with changing the x,y,z to read it into right handed
            //    _convertToRightHandedCoordinateSystem = true;
            //    assimpScene.RootNode.Transform = Matrix4x4.Identity;
            //}
            //else
            //{
            //    var isRightHandedCoordinateSystem = IsRightHandedCoordinateSystem(assimpScene.RootNode);

            //    if (isRightHandedCoordinateSystem.HasValue)
            //        _convertToRightHandedCoordinateSystem = isRightHandedCoordinateSystem.Value;
            //    else
            //        _convertToRightHandedCoordinateSystem = true;
            //}

            ObjectNames = new Dictionary<object, string>();

            if (assimpScene.HasMaterials)
                ConvertMaterials(assimpScene.Materials);
            else
                WpfMaterials = null;


            if (assimpScene.HasMeshes)
                ConvertMeshes(assimpScene.Meshes);


            Model3D model3DGroup;
            if (assimpScene.RootNode != null)
                model3DGroup = CreateWpfModels(assimpScene.RootNode);
            else
                model3DGroup = null;

            return model3DGroup;
        }



        private Model3D CreateWpfModels(Node oneNode)
        {
            Matrix3D matrix;
            Model3D wpfModel3D;

            if (oneNode.HasMeshes && !oneNode.HasChildren && oneNode.MeshIndices.Count == 1)
            {
                // Only single GeometryModel3D
                // Carefull:
                // more than one node can link to one GeometryModel3D
                // if this GeometryModel3D was already used by a previous node, than it has a Transform set
                // In this case create a new GeometryModel3D with same geometry and material

                var meshIndex = oneNode.MeshIndices[0];

                var geometryModel = _wpfModels[meshIndex];
                if (geometryModel.Transform != null)
                {
                    geometryModel = new GeometryModel3D()
                    {
                        Geometry = geometryModel.Geometry,
                        Material = geometryModel.Material,
                        BackMaterial = geometryModel.BackMaterial
                    };

                    if (_createdGeometryModel3Ds != null && _assimpScene != null)
                        _createdGeometryModel3Ds[_assimpScene.Meshes[meshIndex]] = geometryModel;
                }

                wpfModel3D = geometryModel;

                //if (ConvertToRightHandedCoordinateSystem)
                //    matrix = oneNode.Transform.ToRHWpfMatrix3D();
                //else
                //    matrix = oneNode.Transform.ToWpfMatrix3D();

                //geometryModel.Transform = new MatrixTransform3D(matrix);

                //return geometryModel;
            }
            else
            {
                var model3DGroup = new Model3DGroup();

                if (oneNode.HasMeshes)
                {
                    string childMeshName = oneNode.Name;
                    if (childMeshName != null && childMeshName.Length == 0)
                        childMeshName = null; // So we can have only check by null inside next foreach

                    bool hasMoreMeshes = oneNode.MeshIndices.Count > 1;

                    for (int i = 0; i < oneNode.MeshIndices.Count; i++)
                    {
                        var meshIndex = oneNode.MeshIndices[i];
                        var geometryModel3D = _wpfModels[meshIndex];

                        model3DGroup.Children.Add(geometryModel3D);

                        if (childMeshName != null)
                        {
                            string finalName;
                            if (hasMoreMeshes)
                                finalName = string.Format("{0}__{1}", childMeshName, i + 1); // Add mesh count to name
                            else
                                finalName = childMeshName;

                            SetObjectName(geometryModel3D, finalName);
                        }
                    }
                }

                if (oneNode.HasChildren)
                {
                    foreach (var oneChild in oneNode.Children)
                    {
                        var child = CreateWpfModels(oneChild);

                        if (child == null)
                            continue;

                        // Check if we have an empty Model3DGroup - in this case skip this child
                        if (RemoveEmptyModel3DGroups)
                        {
                            var childModel3DGroup = child as Model3DGroup;
                            if (childModel3DGroup != null && childModel3DGroup.Children.Count == 0)
                            {
                                ObjectNames.Remove(childModel3DGroup);
                                continue;
                            }
                        }
                        
                        model3DGroup.Children.Add(child);
                    }
                }

                wpfModel3D = model3DGroup;
            }

            matrix = oneNode.Transform.ToWpfMatrix3D();
            var transform3D = GetTransform(ref matrix);

            if (transform3D != null) // Avoid setting null to Transform - this is still saved and then exported as: <GeometryModel3D Transform="{x:Null}">
                wpfModel3D.Transform = transform3D;


            var name = oneNode.Name;

            if (!string.IsNullOrEmpty(name))
            {
                if (wpfModel3D is Model3DGroup && oneNode.HasMeshes)
                    name += "__Group"; // Child mesh got the name of this node, so we need to add "__Group" to this Model3DGroup

                SetObjectName(wpfModel3D, name);
            }

            return wpfModel3D;
        }

        private Transform3D GetTransform(ref Matrix3D matrix)
        {
            if (matrix.IsIdentity)
                return null; // No need to have a Transform3D object

            // Check if we have complex transform
            if (IsNotZero(matrix.M12) || IsNotZero(matrix.M13) || IsNotZero(matrix.M14) ||
                IsNotZero(matrix.M21) || IsNotZero(matrix.M23) || IsNotZero(matrix.M24) ||
                IsNotZero(matrix.M31) || IsNotZero(matrix.M32) || IsNotZero(matrix.M34))
            {
                return new MatrixTransform3D(matrix);
            }

            if (IsOne(matrix.M11) && IsOne(matrix.M22) && IsOne(matrix.M33))
            {
                // We have simple transform
                return new TranslateTransform3D(matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ);
            }

            if (IsZero(matrix.OffsetX) && IsZero(matrix.OffsetY) && IsZero(matrix.OffsetZ))
            {
                // Simple Scale
                return new ScaleTransform3D(matrix.M11, matrix.M22, matrix.M33);
            }

            // We have scale and translation - we need MatrixTransform3D
            return new MatrixTransform3D(matrix);
        }

        /// <summary>
        /// Returns the GeometryModel3D that was created from the specified assimp mesh object.
        /// </summary>
        /// <param name="mesh">assimp mesh object</param>
        /// <returns>GeometryModel3D that was created from the specified assimp mesh object</returns>
        public GeometryModel3D GetGeometryModel3DForAssimpMesh(Mesh mesh)
        {
            if (mesh == null || _createdGeometryModel3Ds == null)
                return null;

            GeometryModel3D geometryModel3D;

            _createdGeometryModel3Ds.TryGetValue(mesh, out geometryModel3D);
            return geometryModel3D;
        }

        /// <summary>
        /// Returns the Assimp's Mesh object that was used to create the specified GeometryModel3D.
        /// </summary>
        /// <param name="geometryModel3D">GeometryModel3D created from assimp importer</param>
        /// <returns>Assimp's Mesh object that was used to create the specified GeometryModel3D</returns>
        public Mesh GetAssimpMeshForGeometryModel3D(GeometryModel3D geometryModel3D)
        {
            if (geometryModel3D == null || _createdGeometryModel3Ds == null)
                return null;

            foreach (KeyValuePair<Mesh, GeometryModel3D> createdGeometryModel3D in _createdGeometryModel3Ds)
            {
                if (ReferenceEquals(createdGeometryModel3D.Value, geometryModel3D))
                    return createdGeometryModel3D.Key;
            }

            return null; // Not found
        }

        /// <summary>
        /// Returns the WPF Material that was created from the specified assimp material.
        /// </summary>
        /// <param name="assimpMaterial">assimp material</param>
        /// <returns>WPF Material that was created from the specified assimp material</returns>
        public System.Windows.Media.Media3D.Material GetWpfMaterialForAssimpMaterial(Material assimpMaterial)
        {
            if (assimpMaterial == null || _assimpScene == null || _assimpScene.Materials == null || WpfMaterials == null)
                return null;

            int index = _assimpScene.Materials.IndexOf(assimpMaterial);

            if (index == -1)
                return null;

            // For each assimp mesh the appropriate wpf material is created and stored into WpfMaterials list.
            // NOTE: The reason for the list is that assimp's mesh uses index to link it to a material.
            return WpfMaterials[index];
        }

        /// <summary>
        /// Returns the Assimp's Material for the created WPF Material.
        /// </summary>
        /// <param name="wpfMaterial">WPF Material created with assimp importer</param>
        /// <returns>Assimp's Material for the created WPF Material</returns>
        public Material GetAssimpMaterialForWpfMaterial(System.Windows.Media.Media3D.Material wpfMaterial)
        {
            if (wpfMaterial == null || WpfMaterials == null || _assimpScene == null || _assimpScene.Materials == null)
                return null;

            int index = Array.IndexOf(WpfMaterials, wpfMaterial);

            if (index == -1)
                return null;

            // For each assimp mesh the appropriate wpf material is created and stored into WpfMaterials list.
            // NOTE: The reason for the list is that assimp's mesh uses index to link it to a material.
            return _assimpScene.Materials[index];
        }

        private void ConvertMeshes(List<Mesh> meshes)
        {
            _wpfModels = new System.Windows.Media.Media3D.GeometryModel3D[meshes.Count];

            if (_createdGeometryModel3Ds == null)
                _createdGeometryModel3Ds = new Dictionary<Mesh, GeometryModel3D>(meshes.Count);
            else
                _createdGeometryModel3Ds.Clear();

            for (int i = 0; i < meshes.Count; i++)
            {
                var assimpMesh = meshes[i];
                System.Windows.Media.Media3D.MeshGeometry3D wpfMesh = ConvertMesh(assimpMesh);

                int materialIndex = assimpMesh.MaterialIndex;

                System.Windows.Media.Media3D.Material wpfMaterial;
                if (materialIndex >= 0 && materialIndex < WpfMaterials.Length)
                    wpfMaterial = WpfMaterials[materialIndex];
                else
                    wpfMaterial = null;

                var name = assimpMesh.Name;
                if (!string.IsNullOrEmpty(name))
                    SetObjectName(wpfMesh, name);

                var geometryModel3D = new System.Windows.Media.Media3D.GeometryModel3D(wpfMesh, wpfMaterial);

                if (materialIndex >= 0 && materialIndex < _assimpScene.Materials.Count && _assimpScene.Materials[materialIndex].HasTwoSided)
                    geometryModel3D.BackMaterial = wpfMaterial;

                _wpfModels[i] = geometryModel3D;
                _createdGeometryModel3Ds[assimpMesh] = geometryModel3D;
            }
        }

        private void ConvertMaterials(List<Material> materials)
        {
            WpfMaterials = new System.Windows.Media.Media3D.Material[materials.Count];

            for (int i = 0; i < materials.Count; i++)
                WpfMaterials[i] = ConvertMaterial(materials[i]);

            if (_cachedBitmaps != null) // If we used the bitmap cache we can clear it now to release references
                _cachedBitmaps.Clear();
        }

        private System.Windows.Media.Media3D.Material ConvertMaterial(Material assimpMaterial)
        {
            System.Windows.Media.Media3D.Material wpfMaterial;
            System.Windows.Media.Media3D.DiffuseMaterial wpfDiffuseMaterial = null;

            // We check material name if the material is default material (generated by Assimp): http://ehc.ac/p/assimp/discussion/817654/thread/0729fb73/
            if (assimpMaterial.HasName && assimpMaterial.Name == ASSIMP_DEFAULT_MATERIAL_NAME)
                return this.DefaultMaterial;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (assimpMaterial.HasColorEmissive && assimpMaterial.ColorEmissive.R != 0 && assimpMaterial.ColorEmissive.G != 0 && assimpMaterial.ColorEmissive.B != 0)
            {
                var wpfColor = assimpMaterial.ColorEmissive.ToWpfColor();
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(wpfColor));
                var diffuseMaterial = new DiffuseMaterial(Brushes.Black);

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(diffuseMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                return materialGroup;
            }


            if (assimpMaterial.HasTextureDiffuse)
            {
                TextureSlot textureSlot;
                bool isDiffuseTexture = assimpMaterial.GetMaterialTexture(TextureType.Diffuse, 0, out textureSlot);

                if (isDiffuseTexture)
                {
                    var textureFilePath = textureSlot.FilePath;
                    wpfDiffuseMaterial = CreateWpfTextureMaterial(textureFilePath, _texturesPath, _resolveResourceFunc);
                }
            }

            // This does not work correctly in some cases - for example for "my 3d objects\my\test two sided materials\two_sided.3DS"
            //if (material.HasColorDiffuse)
            //{
            //    var wpfColor = material.ColorDiffuse.ToWpfColor(alpha: 1.0f);
            //    wpfDiffuseMaterial.Color = wpfColor;
            //}

            //if (material.HasColorAmbient)
            //{
            //    var wpfColor = material.ColorAmbient.ToWpfColor(alpha: 1.0f);
            //    wpfDiffuseMaterial.AmbientColor = wpfColor;
            //}


            if (wpfDiffuseMaterial == null && assimpMaterial.HasColorDiffuse)
            {
                var wpfColor = assimpMaterial.ColorDiffuse.ToWpfColor();

                if (wpfColor.A == 0)
                {
                    // Workaround for a bug in importer
                    // if diffuse color is specified only as 3 floats (12 bytes) than alpha stays at 0 value instead of having its value 1

                    var diffuseProperty = assimpMaterial.GetNonTextureProperty("$clr.diffuse");
                    if (diffuseProperty != null && diffuseProperty.ByteCount == 12)
                        wpfColor.A = 255;
                }

                if (assimpMaterial.Opacity < 1.0f)
                    wpfColor.A = (byte)(wpfColor.A * assimpMaterial.Opacity);

                wpfDiffuseMaterial = new System.Windows.Media.Media3D.DiffuseMaterial(new SolidColorBrush(wpfColor));
            }


            if (assimpMaterial.HasShininess && assimpMaterial.HasColorSpecular && assimpMaterial.Shininess > 0 && assimpMaterial.ColorSpecular != _blackColor4D)
            {
                var wpfSpecularColor = assimpMaterial.ColorSpecular.ToWpfColor();

                var wpfSpecularMaterial = new SpecularMaterial(new SolidColorBrush(wpfSpecularColor), assimpMaterial.Shininess);

                if (assimpMaterial.ShininessStrength < 1)
                {
                    byte colorLevel = (byte)(255.0f * assimpMaterial.ShininessStrength);
                    wpfSpecularMaterial.Color = Color.FromRgb(colorLevel, colorLevel, colorLevel);
                }

                if (wpfDiffuseMaterial != null)
                {
                    var materialGroup = new MaterialGroup();
                    materialGroup.Children.Add(wpfDiffuseMaterial);
                    materialGroup.Children.Add(wpfSpecularMaterial);

                    wpfMaterial = materialGroup;
                }
                else
                {
                    wpfMaterial = wpfSpecularMaterial;
                }
            }
            else
            {
                if (wpfDiffuseMaterial != null)
                    wpfMaterial = wpfDiffuseMaterial;
                else
                    wpfMaterial = null;
            }


            if (wpfMaterial == null)
                wpfMaterial = DefaultMaterial;
            else if (assimpMaterial.HasName && !string.IsNullOrEmpty(assimpMaterial.Name))
                SetObjectName(wpfMaterial, assimpMaterial.Name); // Set material's name from assimp name

                return wpfMaterial;
        }

        /// <summary>
        /// CreateWpfTextureMaterial method creates a DiffuseMaterial from texture file name.
        /// </summary>
        /// <param name="textureFileName">textureFileName as written in model file</param>
        /// <param name="texturesFilePath">textures path as specified to the AssimpWpfConverter (can be null)</param>
        /// <param name="resolveResourceFunc">callback function to resolve textureFileName to stream (can be null)</param>
        /// <returns>DiffuseMaterial with ImageBrush</returns>
        protected virtual DiffuseMaterial CreateWpfTextureMaterial(string textureFileName, string texturesFilePath, Func<string, Stream> resolveResourceFunc)
        {
            DiffuseMaterial wpfDiffuseMaterial = null;
            BitmapSource bitmapImage;

            if (_cachedBitmaps == null)
                _cachedBitmaps = new Dictionary<string, BitmapSource>();

            if (!_cachedBitmaps.TryGetValue(textureFileName, out bitmapImage)) // Check if already cached
            {
                // This textureFileName was not cached yet

                // If resolveResourceFunc is specified, it is always called
                Stream textureStream;
                if (resolveResourceFunc != null)
                    textureStream = resolveResourceFunc(textureFileName);
                else
                    textureStream = null;

                string finalTextureFileName;
                if (textureStream == null)
                {
                    textureFileName = textureFileName.Replace('/', '\\');

                    // Remove starting ".\" for example in duck.dae we have ".\duckCM.png"
                    if (textureFileName.StartsWith(".\\"))
                        textureFileName = textureFileName.Substring(2);

                    // Correctly handle the texture paths that start with "\\.." - in this case the leading '\' needs to be removed
                    // For example in "Assimp test models\Collada\earthCylindrical.DAE" the path to the texture is set as:
                    // <init_from>\..\LWO\LWO2\MappingModes\EarthCylindric.jpg</init_from>
                    if (textureFileName.StartsWith("\\.."))
                        textureFileName = textureFileName.Substring(1);

                    if (!System.IO.Path.IsPathRooted(textureFileName) && !string.IsNullOrEmpty(texturesFilePath))
                        textureFileName = System.IO.Path.Combine(texturesFilePath, textureFileName);

                    // Check if file extension is supported and if file exist - if not than try to get a replacement file - the same file name with supported extension
                    finalTextureFileName = CheckTextureFileName(textureFileName);

                    if (finalTextureFileName == null)
                    {
                        // If we are here then the texture file does not exist or is not supported.
                        // If file does not exist, then we also check directly in the texturesFilePath (in case the path to the texture file is not correct)
                        if (!string.IsNullOrEmpty(texturesFilePath))
                        {
                            textureFileName = System.IO.Path.GetFileName(textureFileName);
                            textureFileName = System.IO.Path.Combine(texturesFilePath, textureFileName);

                            finalTextureFileName = CheckTextureFileName(textureFileName);
                        }

                        if (finalTextureFileName == null)
                            return null;
                    }
                }
                else
                {
                    finalTextureFileName = null;
                }

                // Create bitmap
                if (textureStream != null)
                    bitmapImage = CreateWpfBitmap(textureStream);
                else if (finalTextureFileName != null)
                    bitmapImage = CreateWpfBitmap(finalTextureFileName);
                else
                    bitmapImage = null; // This should not happen


                // Cache the bitmap
                if (bitmapImage != null)
                    _cachedBitmaps[textureFileName] = bitmapImage;
            }

            // Create DiffuseMaterial
            if (bitmapImage != null)
                wpfDiffuseMaterial = CreateTextureMaterial(bitmapImage, textureFileName);

            return wpfDiffuseMaterial;
        }

        /// <summary>
        /// Creates BitmapSource from textureFileName.
        /// </summary>
        /// <param name="textureFileName">texture file name and its full path</param>
        /// <returns>BitmapSource</returns>
        protected virtual BitmapSource CreateWpfBitmap(string textureFileName)
        {
            BitmapImage bitmapImage;

            try
            {
                bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = this.BitmapCacheOption;
                bitmapImage.UriSource = new Uri(textureFileName);
                bitmapImage.EndInit();
            }
            catch (Exception ex)
            {
                LogMessage("Error reading texture file: " + ex.Message, "FileName: " + textureFileName);
                bitmapImage = null;
            }

            return bitmapImage;
        }

        /// <summary>
        /// Creates BitmapSource from file stream.
        /// </summary>
        /// <param name="fileStream">file stream</param>
        /// <returns>BitmapSource</returns>
        protected virtual BitmapSource CreateWpfBitmap(Stream fileStream)
        {
            BitmapImage bitmapImage;

            try
            {
                bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = this.BitmapCacheOption;
                bitmapImage.StreamSource = fileStream;
                bitmapImage.EndInit();
            }
            catch (Exception ex)
            {
                LogMessage("Error reading texture from stream: " + ex.Message, "");
                bitmapImage = null;
            }

            return bitmapImage;
        }

        /// <summary>
        /// Creates DiffuseMaterial from bitmap (textureFileName is provided as a reference)
        /// </summary>
        /// <param name="bitmap">BitmapSource</param>
        /// <param name="textureFileName">textureFileName as written in model file</param>
        /// <returns>DiffuseMaterial</returns>
        protected virtual DiffuseMaterial CreateTextureMaterial(BitmapSource bitmap, string textureFileName)
        {
            var imageBrush = new ImageBrush(bitmap)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile,
                //Transform = _textureImageTransform // = new MatrixTransform(1, 0, 0, -1, 0, 1) // We have already flipped the Y coordinate of TextureCoordinates so no need for transformation there
            };

            var wpfDiffuseMaterial = new DiffuseMaterial(imageBrush);

            return wpfDiffuseMaterial;
        }

        /// <summary>
        /// CheckTextureFileName checks the texture file name and if file does not exist or is not in supported file format tries to find another file that can be used instead.
        /// Returns the file name that can be used to create WPF texture. If no file can be found, null is returned.
        /// The method can be overridden.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>CheckTextureFileName</b> checks the texture file name and if file does not exist or is not in supported file format tries to find another file that can be used instead.
        /// Returns the file name that can be used to create WPF texture. If no file can be found, null is returned.
        /// </para>
        /// <para>
        /// The method first checks if file extension is supported image format for WPF.
        /// </para>
        /// <para>
        /// If file format is supported, then method checks if file exist on disk. If it exists then we can use this file and the original textureFileName is returned.
        /// If file does not exist, then it is checked if the file name was written in old 8.3 file name format, but on disk a file with longer name exist. In this case the existing file name is returned.
        /// </para>
        /// <para>
        /// If file format is not supported, then it is checked if there is another file on disk with supported file format. In that case the supported file name is returned.
        /// </para>
        /// </remarks>
        /// <param name="textureFileName">full path and file name for texture file</param>
        /// <returns>file name that can be used to create WPF texture or null if no file can be found</returns>
        protected virtual string CheckTextureFileName(string textureFileName)
        {
            string filePath, fileWithoutExtension;

            var fileExtension = System.IO.Path.GetExtension(textureFileName);
            if (string.IsNullOrEmpty(fileExtension))
                return null;

            if (SUPPORTED_IMAGE_FORMATS.IndexOf(fileExtension.ToLower()) != -1) // file format supported?
            {
                // Image format supported by WPF
                // Check if file exist
                if (System.IO.File.Exists(textureFileName))
                    return textureFileName; // everything ok

                // Does not exist - maybe it is 8.3 file name - find the full file name
                filePath = System.IO.Path.GetDirectoryName(textureFileName);

                if (filePath != null && System.IO.Directory.Exists(filePath))
                {
                    fileWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(textureFileName);

                    if (fileWithoutExtension.Length == 8) // using 8.3 file name
                    {
                        var possibleFiles = System.IO.Directory.GetFiles(filePath, fileWithoutExtension + "*" + fileExtension); // note: fileExtension already contains '.'

                        if (possibleFiles != null && possibleFiles.Length > 0)
                        {
                            LogMessage("Using full file name instead of 8.3 format", string.Format("Using '{0}' instead of '{1}'", possibleFiles[0], textureFileName));
                            return possibleFiles[0]; // use the first found file
                        }
                    }
                }

                return null;
            }


            // UH: Not supported file format
            // Try to find file with same name but different file format

            string newTextureFileName;

            filePath = System.IO.Path.GetDirectoryName(textureFileName);
            fileWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(textureFileName);

            string fullPathWithoutExtension = System.IO.Path.Combine(filePath, fileWithoutExtension);

            if (System.IO.File.Exists(fullPathWithoutExtension + ".png"))
            {
                newTextureFileName = fullPathWithoutExtension + ".png";
            }
            else if (System.IO.File.Exists(fullPathWithoutExtension + ".jpg"))
            {
                newTextureFileName = fullPathWithoutExtension + ".jpg";
            }
            else if (System.IO.File.Exists(fullPathWithoutExtension + ".gif"))
            {
                newTextureFileName = fullPathWithoutExtension + ".gif";
            }
            else if (System.IO.File.Exists(fullPathWithoutExtension + ".bmp"))
            {
                newTextureFileName = fullPathWithoutExtension + ".bmp";
            }
            else
            {
                newTextureFileName = null;
                LogMessage(LOGGER_PREFIX + "Unknown image file format", string.Format("WPF cannot read images with {0} file extension. Convert the image into a supported file format and put it in the same directory as original image.", fileExtension));
            }

            if (newTextureFileName != null)
            {
                LogMessage("Changed texture file extension", string.Format("Using '{0}' instead of '{1}'", newTextureFileName, textureFileName));
                return textureFileName; // Found supported file
            }

            return null; // Supported file not found
        }

        private System.Windows.Media.Media3D.MeshGeometry3D ConvertMesh(Mesh assimpMesh)
        {
            var meshGeometry3D = new System.Windows.Media.Media3D.MeshGeometry3D();


            var vertexCount = assimpMesh.VertexCount;
            var point3DCollection = new System.Windows.Media.Media3D.Point3DCollection(vertexCount);

            var vertices = assimpMesh.Vertices;

            //if (_convertToRightHandedCoordinateSystem)
            //{
            //    for (int i = 0; i < vertexCount; i++)
            //        point3DCollection.Add(vertices[i].ToRHWpfPoint3D());
            //}
            //else
            //{
                for (int i = 0; i < vertexCount; i++)
                    point3DCollection.Add(vertices[i].ToWpfPoint3D());
            //}

            meshGeometry3D.Positions = point3DCollection;


            if (assimpMesh.HasNormals)
            {
                var normals = assimpMesh.Normals;
                var normalsCount = normals.Count;
                var normalsCollection = new System.Windows.Media.Media3D.Vector3DCollection(normalsCount);

                for (int i = 0; i < normalsCount; i++)
                    normalsCollection.Add(normals[i].ToWpfVector3D());

                meshGeometry3D.Normals = normalsCollection;
            }

            if (assimpMesh.HasTextureCoords(0))
            {
                var textureCoordinates = assimpMesh.TextureCoordinateChannels[0];
                int textureCoordinatesCount = textureCoordinates.Count;
                var textureCollection = new PointCollection(textureCoordinatesCount);

                // We also flip Y texture coordinates
                // In Assimp UV coordinate system has its origin in the lower-left corner
                // In WPF the origin is at upper-left
                for (int i = 0; i < textureCoordinatesCount; i++)
                    textureCollection.Add(new Point(textureCoordinates[i].X, 1 - textureCoordinates[i].Y));

                meshGeometry3D.TextureCoordinates = textureCollection;
            }

            if (assimpMesh.HasFaces)
            {
                if (_isAlreadyTriangulated)
                {
                    meshGeometry3D.TriangleIndices = new Int32Collection(assimpMesh.GetIndices());
                }
                else
                {
                    int facesCount = assimpMesh.FaceCount;
                    var assimpMeshFaces = assimpMesh.Faces;

                    Int32Collection polygonIndices;

                    if (ReadPolygonIndices && _polygonIndicesProperty != null)
                        polygonIndices = new Int32Collection(facesCount * 3);
                    else
                        polygonIndices = null;

                    List<int> wpfIndices = new List<int>(facesCount * 3);

                    for (int i = 0; i < facesCount; i++)
                    {
                        var assimpMeshFace = assimpMeshFaces[i];

                        var indices = assimpMeshFace.Indices;
                        var indicesCount = assimpMeshFace.IndexCount;

                        if (indicesCount == 3)
                        {
                            wpfIndices.AddRange(assimpMeshFace.Indices);
                        }
                        else if (indicesCount == 4)
                        {
                            // Optimized code for 4 indices that are used in most of the cases (usually meshes use quads)

                            if (UseSimpleTriangulation)
                            {
                                int firstIndice = indices[0];

                                wpfIndices.Add(firstIndice);
                                wpfIndices.Add(indices[1]);
                                wpfIndices.Add(indices[2]);

                                wpfIndices.Add(firstIndice);
                                wpfIndices.Add(indices[2]);
                                wpfIndices.Add(indices[3]);
                            }
                            else
                            {
                                // If we use regular triangulation,
                                // we can simplify the case for 4 indices with
                                // first finding the indice where the concave point is (if there is any).
                                // In case of finding the concave point we start with it and do a triangle fan from it.
                                // If concave point is not found, then we do triangle fan from the first indice.
                                var p1 = point3DCollection[indices[0]];
                                var p2 = point3DCollection[indices[1]];
                                var p3 = point3DCollection[indices[2]];
                                var p4 = point3DCollection[indices[3]];

                                var v1 = p2 - p1;
                                var v2 = p3 - p2;
                                var v3 = p4 - p3;
                                var v4 = p1 - p4;

                                var n1 = Vector3D.CrossProduct(v1, v2);
                                var n2 = Vector3D.CrossProduct(v2, v3);
                                var n3 = Vector3D.CrossProduct(v3, v4);
                                var n4 = Vector3D.CrossProduct(v4, v1);

                                var summedNormal = n1 + n2 + n3 + n4;

                                int startIndex;
                                if (Vector3D.DotProduct(n2, summedNormal) < 0)
                                    startIndex = 1;
                                else if (Vector3D.DotProduct(n3, summedNormal) < 0)
                                    startIndex = 2;
                                else if (Vector3D.DotProduct(n3, summedNormal) < 0)
                                    startIndex = 3;
                                else
                                    startIndex = 0;

                                wpfIndices.Add(indices[startIndex]);
                                wpfIndices.Add(indices[(startIndex + 1) % 4]);
                                wpfIndices.Add(indices[(startIndex + 2) % 4]);

                                wpfIndices.Add(indices[startIndex]);
                                wpfIndices.Add(indices[(startIndex + 2) % 4]);
                                wpfIndices.Add(indices[(startIndex + 3) % 4]);
                            }
                        }
                        else if (indicesCount > 4)
                        {
                            if (UseSimpleTriangulation)
                            {
                                // Triangulate with using simple triangle fan (works for simple polygons without holes)
                                int firstIndice = indices[0];
                                for (int j = 1; j < indicesCount - 1; j++)
                                {
                                    wpfIndices.Add(firstIndice);
                                    wpfIndices.Add(indices[j]);
                                    wpfIndices.Add(indices[j + 1]);
                                }
                            }
                            else
                            {
                                // Get the triangulator Func (by default the triangulator from Ab3d.PowerToys will be used)
                                var triangulator = GetTriangulator();
                                
                                if (triangulator != null)
                                {
                                    // To triangulate 3D positions, we first convert 3D positions to 2D positions.
                                    // This is done with project the positions onthe the 2D plane (we assume that all positions for one face lie on the same plane).
                                    var positions2D = Project3DPositionTo2D(point3DCollection, indices);

                                    var triangleIndices = triangulator(positions2D);

                                    //var triangulator3D = new Ab3d.Assimp.Common.Triangulator(positions2D);
                                    //triangleIndices = triangulator3D.CreateTriangleIndices();

                                    // We got triangleIndices with indexes from 0 to triangleIndices.Count
                                    // Now we need to adjust the indexes to the real indexes from indices collection (those are in range from 0 to positions.count)
                                    var triangleIndicesCount = triangleIndices.Count;
                                    for (int j = 0; j < triangleIndicesCount; j++)
                                        triangleIndices[j] = indices[triangleIndices[j]];

                                    wpfIndices.AddRange(triangleIndices);
                                }
                                else
                                {
                                    // When there is no special triangulator set, then use simple triangle fan
                                    int firstIndice = indices[0];
                                    for (int j = 1; j < indicesCount - 1; j++)
                                    {
                                        wpfIndices.Add(firstIndice);
                                        wpfIndices.Add(indices[j]);
                                        wpfIndices.Add(indices[j + 1]);
                                    }
                                }
                            }
  
                        }
                        // else if < 3 just skip this face

                        // If we are reading polygon indices do that now
                        if (polygonIndices != null)
                        {
                            int firstIndex = indices[0];
                            polygonIndices.Add(firstIndex);

                            for (int j = 1; j < indicesCount; j++)
                            {
                                int currentIndex = indices[j];
                                
                                if (currentIndex != firstIndex) // prevent duplicating first index - this could lead to premature closing of the polygon
                                    polygonIndices.Add(currentIndex);
                            }

                            // Close the polygon with adding the first index
                            polygonIndices.Add(firstIndex);
                        }
                    }

                    meshGeometry3D.TriangleIndices = new Int32Collection(wpfIndices);

                    if (polygonIndices != null)
                        meshGeometry3D.SetValue(_polygonIndicesProperty, polygonIndices);
                }
            }

            return meshGeometry3D;
        }

        private Func<List<Point>, List<int>> GetTriangulator()
        {
            // Use CustomTriangulator when set
            if (TriangulatorFunc != null)
                return TriangulatorFunc;

            if (_triangulatorType == null)
            {
                // Load Triangulator class and its CreateTriangleIndices method from Ab3d.PowerToys by using reflection
                _triangulatorType = Type.GetType("Ab3d.Utilities.Triangulator, Ab3d.PowerToys", throwOnError: false);

                if (_triangulatorType == null)
                    return null;

                if (_createTriangleIndicesMethodInfo == null)
                    _createTriangleIndicesMethodInfo = _triangulatorType.GetMethod("CreateTriangleIndices");

                _powerToysTriangulator = new Func<List<Point>, List<int>>(delegate (List<Point> positions)
                {
                    // Execute:
                    //var triangulatorInstance = new Ab3d.Utilities.Triangulator(positions);
                    //triangleIndices = triangulator3D.CreateTriangleIndices();

                    var triangulatorInstance = Activator.CreateInstance(_triangulatorType, positions);
                    var triangleIndices = (List<int>)_createTriangleIndicesMethodInfo.Invoke(triangulatorInstance, null);

                    return triangleIndices;
                });
            }

            return _powerToysTriangulator;
        }

        private void EnsurePolygonIndicesProperty()
        {
            // Use reflection to get the PolygonIndicesProperty.
            // This is done because we do not want that this library require Ab3d.PowerToys library just because of one DependencyProperty
            if (!_isPolygonIndicesPropertyCreated)
            {
                try
                {
                    // First check if Ab3d.PowerToys library is available and gets base type for PolygonIndicesProperty
                    var type = Type.GetType("Ab3d.Utilities.MeshUtils, Ab3d.PowerToys", throwOnError: false);

                    if (type != null)
                    {
                        var fieldInfo = type.GetField("PolygonIndicesProperty", BindingFlags.Static | BindingFlags.Public);
                        if (fieldInfo != null)
                        {
                            _polygonIndicesProperty = fieldInfo.GetValue(null) as DependencyProperty;
                        }
                    }
                }
                catch
                {
                    // This should not happen
                }

                _isPolygonIndicesPropertyCreated = true;
            }
        }


        private void SetObjectName(DependencyObject dependencyObject, string name)
        {
            if (dependencyObject == null || string.IsNullOrEmpty(name))
                return;

            if (ObjectNames != null)
                ObjectNames[dependencyObject] = name;


            // The following code is the same as in Ab3d.PowerToys's SetName method:
            string correctedName;
            bool isOriginalNameCorrect = IsXamlNameCorrect(ref name, out correctedName);

            if (!isOriginalNameCorrect)
                name = correctedName;

            try
            {
                dependencyObject.SetValue(FrameworkElement.NameProperty, name);
            }
            catch
            { }
        }

        // This method is copied from Ab3d.PowerToys's Ab3d.Extensions class:

        // Name must start with a letter or underscore and can contain only letters, digits, or underscores.
        private static bool IsXamlNameCorrect(ref string originalName, out string correctedName)
        {
            bool isNameOk;
            char oneChar;

            if (string.IsNullOrEmpty(originalName))
            {
                correctedName = null;
                return true;
            }


            isNameOk = true;

            // First check if name is ok
            for (int i = 0; i < originalName.Length; i++)
            {
                oneChar = originalName[i];

                if (!char.IsLetterOrDigit(oneChar) && oneChar != '_')
                {
                    isNameOk = false;
                    break;
                }
            }

            if (!isNameOk)
            {
                StringBuilder sb;

                sb = new StringBuilder(originalName.Length);

                // If the first character is not letter or underscores - add '_' to the name
                if (!char.IsLetter(originalName[0]) && originalName[0] != '_')
                    sb.Append('_');

                // Name must start with a letter or underscore and can contain only letters, digits, or underscores.
                for (int i = 0; i < originalName.Length; i++)
                {
                    oneChar = originalName[i];

                    if (char.IsLetterOrDigit(oneChar) || oneChar == '_')
                        sb.Append(oneChar);
                    else
                        sb.Append('_');
                }

                correctedName = sb.ToString();
            }
            else
            {
                // Name does not contain wrong characters - just check if it starts with letter or underscore
                if (!char.IsLetter(originalName[0]) && originalName[0] != '_')
                {
                    correctedName = '_' + originalName;
                    isNameOk = false;
                }
                else
                {
                    correctedName = null;
                }
            }


            return isNameOk;
        }



        private void LogMessage(string msg, string data = "")
        {
            if (LoggerCallback != null)
                LoggerCallback(msg, data);
        }

        
        private const double EPSILON = 2.2204460492503131E-15;

        private static bool IsOne(double value)
        {
            return (Math.Abs((double)(value - 1.0)) < EPSILON);
        }

        private static bool IsZero(double value)
        {
            return (Math.Abs(value) < EPSILON);
        }

        private static bool IsNotZero(double value)
        {
            return (Math.Abs(value) > EPSILON);
        }

        private static bool IsOne(float value)
        {
            return (Math.Abs(value - 1.0) < EPSILON);
        }

        private static bool IsZero(float value)
        {
            return (Math.Abs(value) < EPSILON);
        }

        private static bool IsNotZero(float value)
        {
            return (Math.Abs(value) > EPSILON);
        }

        private static List<Point> Project3DPositionTo2D(Point3DCollection positions, List<int> polygonIndices)
        {
            if (positions == null || positions.Count < 3)
                throw new ArgumentException("positions is null or have less then 3 element");

            if (polygonIndices == null || polygonIndices.Count < 3)
                throw new ArgumentException("polygonIndices is null or have less then 3 element");

            var positionsCount = polygonIndices.Count;
            var positions2D = new List<Point>(positionsCount);

            // We assume that the position lie on the same plane
            // so we do not need to calculate the proper polygon normal
            // but can assume that the normal of the first triangle will be sufficient to determine the orientation.
            // Once we have the orientation, we can do simple projection to 2D positions with eliminating the axis that have the biggest normal value
            // For example if normal is up (0,1,0), this means that all positions lie on the same xz plane and that all y values are the same => we can remove the y value

            var p1 = positions[polygonIndices[0]];
            var p2 = positions[polygonIndices[1]];
            var p3 = positions[polygonIndices[2]];

            var v1 = p1 - p2;
            var v2 = p3 - p2;

            // We use absolute normal values.
            var normal = Vector3D.CrossProduct(v1, v2);
            var nx = Math.Abs(normal.X);
            var ny = Math.Abs(normal.Y);
            var nz = Math.Abs(normal.Z);

            if (nx > ny && nx > nz)
            {
                // normal.x is the biggest => remove x values
                for (int i = 0; i < positionsCount; i++)
                {
                    var position3D = positions[polygonIndices[i]];
                    positions2D.Add(new Point(position3D.Y, position3D.Z));
                }
            }
            else if (ny > nz)
            {
                // normal.y is the biggest => remove y values
                for (int i = 0; i < positionsCount; i++)
                {
                    var position3D = positions[polygonIndices[i]];
                    positions2D.Add(new Point(position3D.X, position3D.Z));
                }
            }
            else
            {
                // normal.z is the biggest => remove z values
                for (int i = 0; i < positionsCount; i++)
                {
                    var position3D = positions[polygonIndices[i]];
                    positions2D.Add(new Point(position3D.X, position3D.Y));
                }
            }

            return positions2D;
        }
    }
}