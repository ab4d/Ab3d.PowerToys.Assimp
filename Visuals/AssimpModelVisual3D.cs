using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.ComponentModel;
using System.Diagnostics;
using Ab3d.Assimp;

namespace Ab3d.Visuals
{
    /// <summary>
    /// AssimpModelVisual3D is a Visual3D class that shows 3D models that are read from almost any 3D file format with Assimp library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AssimpModelVisual3D is a Visual3D class that shows 3D models that are read from almost any 3D file format with Assimp library.
    /// </para>
    /// <para>
    /// The source of the obj file is specified with <see cref="Source"/> property.
    /// </para>
    /// <para>
    /// The read 3D model is positioned according to the <see cref="Position"/> and <see cref="PositionType"/> properties.
    /// </para>
    /// <para>
    /// If the PositionType is set to Center (default), the 3D model will be position so that its center will be at the Position coordinates.
    /// If the PositionType is set to BottomCenter, the 3D model will be positioned above the Position coordinates.
    /// </para>
    /// <para>
    /// The size of the shown object is controlled by the <see cref="SizeX"/>, <see cref="SizeY"/>, <see cref="SizeZ"/> and <see cref="PreserveScaleAspectRatio"/> properties.
    /// </para>
    /// <para>
    /// By default all the SizeX, SizeY and SizeZ are set to -1. This means that the original size of the object is used. But if the SizeX is set to let's say 100, the object would be scaled so its SizeX would be 100.
    /// </para>
    /// <para>
    /// If PreserveScaleAspectRatio is true (default), than the aspect ratio of the 3D model is preserved. This means that the model is stretched to one side mode than to the other - the scale is evenly set to all the axis. This also means that if all SizeX, SizeY and SizeZ are defined, the object will be scaled so it will not exceed and of the specified sizes. 
    /// </para>
    /// <para>
    /// If PreserveScaleAspectRatio is false, than the aspect ration of the 3D model will not be preserved. In this case the SizeX, SizeY and SizeZ will be applied so the object will be exactly the size of the specified sizes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>
    /// Before using AssimpModelVisual3D the following namespace declaration has to be added to the root xaml element:
    /// </para>
    /// <code>
    /// xmlns:assimpVisuals="clr-namespace:Ab3d.Visuals;assembly=Ab3d.PowerToys.Assimp"
    /// </code>
    /// <para>
    /// Now the AssimpModelVisual3D can be used in XAML. The following example shown a 3D model from ab3d.dae file. The model's center is positioned at (-250, 0, 0) and its size is set so its SizeX is 80:
    /// </para>
    /// <code lang="xaml">
    /// &lt;assimpVisuals:AssimpModelVisual3D Source="/Resources/ab3d.dae" 
    ///                SizeX="80"
    ///                Position="0 40 0" PositionType="Center"/&gt;
    /// </code>
    /// <para>
    /// The following code shows the same mode, but this time it is custom sized:
    /// </para>
    /// <code lang="xaml">
    /// &lt;assimpVisuals:AssimpModelVisual3D Source="/Resources/ab3d.dae" 
    ///                SizeX="50" SizeY="30" SizeZ="40"
    ///                PreserveScaleAspectRatio="False"
    ///                Position="0 40 0" PositionType="Center"/&gt;
    /// </code>
    /// </example>
    [DefaultProperty("Source")]
    public class AssimpModelVisual3D : ModelVisual3D, ISupportInitialize, IUriContext
    {
        /// <summary>
        /// VisualPositionType defines the type of Position that is used in AssimpModelVisual3D.
        /// </summary>
        public enum VisualPositionType
        {
            /// <summary>
            /// Objects center is used to position the 3D model.
            /// </summary>
            Center = 0,

            /// <summary>
            /// The 3D models will be placed above the Position coordinates.
            /// </summary>
            BottomCenter
        }

        #region private and properted properties
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private bool _isModelRead;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Model3D _rootModel3D;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private bool _isSourceDirty;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Rect3D _originalObjectBounds;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string _folderPath;
        
        /// <summary>
        /// Transform3DGroup that is used to scale and position the model
        /// </summary>
        protected Transform3DGroup modelTransformGroup;

        /// <summary>
        /// TranslateTransform3D that is used to position the model
        /// </summary>
        protected TranslateTransform3D modelTranslate;

        /// <summary>
        /// ScaleTransform3D that is used to scale the model
        /// </summary>
        protected ScaleTransform3D modelScale;

        /// <summary>
        /// savedHiddenContent stored the content of this model when it is hidden by setting IsVisible to false.
        /// </summary>
        protected Model3D savedHiddenContent;
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public AssimpModelVisual3D()
            : base()
        {
            modelTranslate = new TranslateTransform3D();
            modelScale = new ScaleTransform3D();

            modelTransformGroup = new Transform3DGroup();
            modelTransformGroup.Children.Add(modelTranslate);
            modelTransformGroup.Children.Add(modelScale);

            _isModelRead = false;
            _isSourceDirty = true;
        }

        /// <summary>
        /// Gets a Ab3d.AssimpWpfImporter instance that is used to read the model file.
        /// </summary>
        public AssimpWpfImporter UsedAssimpWpfImporter { get; protected set; }


        #region IsVisibleProperty
        /// <summary>
        /// IsVisibleProperty
        /// </summary>
        public static readonly DependencyProperty IsVisibleProperty =
            DependencyProperty.Register("IsVisible", typeof(bool), typeof(AssimpModelVisual3D),
                new PropertyMetadata(true, OnIsVisiblePropertyChanged));

        /// <summary>
        /// Gets or sets a Boolean that specify if the object is visible.
        /// </summary>
        public bool IsVisible
        {
            get { return (bool)GetValue(IsVisibleProperty); }
            set { SetValue(IsVisibleProperty, value); }
        }

        /// <summary>
        /// OnIsVisiblePropertyChanged
        /// </summary>
        /// <param name="obj">DependencyObject</param>
        /// <param name="args">DependencyPropertyChangedEventArgs</param>
        private static void OnIsVisiblePropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            var baseVisual3D = (AssimpModelVisual3D)obj;
            bool newValue = (bool)args.NewValue;

            baseVisual3D.OnIsVisibleChanged(newValue);
        }

        /// <summary>
        /// OnIsVisibleChanged is called when the IsVisible property is changed
        /// </summary>
        /// <param name="newIsVisibleValue">newIsVisibleValue as bool</param>
        protected virtual void OnIsVisibleChanged(bool newIsVisibleValue)
        {
            if (newIsVisibleValue)
            {
                // IsVisible == true
                if (savedHiddenContent != null)
                {
                    // The content was saved 
                    Content = savedHiddenContent;
                    savedHiddenContent = null;
                }
                else
                {
                    // There were no saved content or some properties were changed during being invisible => recreate the model
                    // UpdateContentIfNotInizializing will set the baseLineVisual3D.Content

                    if (Content == null)
                        Content = new GeometryModel3D(); // Content was set to null because IsVisible was set to false - recreate an empty content now

                    UpdateContentIfNotInitializing(); // And now we can also recreate the geometry and material
                }
            }
            else
            {
                // IsVisible == false 

                // Save the current content and set it to null - showing nothing
                savedHiddenContent = Content;
                Content = null;
            }
        }
        #endregion

        #region SourceProperty
        /// <summary>
        /// Gets or sets the Source of the obj file
        /// </summary>
        public Uri Source
        {
            get { return (Uri)GetValue(SourceProperty); }
            set { SetValue(SourceProperty, value); }
        }

        /// <summary>
        /// SourceProperty
        /// </summary>
        public static readonly DependencyProperty SourceProperty = 
                                    DependencyProperty.Register("Source", typeof(Uri), typeof(AssimpModelVisual3D),
                                    new FrameworkPropertyMetadata(null, new PropertyChangedCallback(AssimpModelVisual3D.OnSourceChanged)));


        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var AssimpModelVisual3D = (AssimpModelVisual3D)d;

            AssimpModelVisual3D._isSourceDirty = true;
            AssimpModelVisual3D.UpdateContentIfNotInitializing();
        }
        #endregion

        #region TexturesPathProperty
        /// <summary>
        /// Gets or sets the path where the textures are located. If null or "" the path of the obj file is used.
        /// It is also possible to set TexturesPath to url of the textures (http://...) or to the application resources ("pack://application:,,,/XAMLBrowserApplication1;component/models")
        /// </summary>
        public string TexturesPath
        {
            get { return (string)GetValue(TexturesPathProperty); }
            set { SetValue(TexturesPathProperty, value); }
        }

        /// <summary>
        /// TexturesPathProperty
        /// </summary>
        public static readonly DependencyProperty TexturesPathProperty =
            DependencyProperty.Register("TexturesPath", typeof(string), typeof(AssimpModelVisual3D),
            new FrameworkPropertyMetadata(null, new PropertyChangedCallback(AssimpModelVisual3D.OnTexturesPathChanged)));

        private static void OnTexturesPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var AssimpModelVisual3D = (AssimpModelVisual3D)d;

            if (!AssimpModelVisual3D.isInitializing)
                AssimpModelVisual3D.Reload();
        }
        #endregion

        #region PositionProperty
        /// <summary>
        /// Gets or sets the Position of the read obj model. The type of position is determined by <see cref="PositionType"/> property.
        /// </summary>
        public Point3D Position
        {
            get { return (Point3D)GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }

        /// <summary>
        /// PositionProperty
        /// </summary>
        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register("Position", typeof(Point3D), typeof(AssimpModelVisual3D),
                new PropertyMetadata(new Point3D(0, 0, 0), OnScaleOrPositionChanged));

        private static void OnScaleOrPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var AssimpModelVisual3DVisual3D = (AssimpModelVisual3D)d;

            AssimpModelVisual3DVisual3D.UpdateScaleAndCenter();
        }
        #endregion

        #region PositionTypeProperty
        /// <summary>
        /// Gets or sets the <see cref="VisualPositionType"/> value that specifies the type of the <see cref="Position"/>
        /// </summary>
        public VisualPositionType PositionType
        {
            get { return (VisualPositionType)GetValue(PositionTypeProperty); }
            set { SetValue(PositionTypeProperty, value); }
        }

        /// <summary>
        /// PositionTypeProperty
        /// </summary>
        public static readonly DependencyProperty PositionTypeProperty =
            DependencyProperty.Register("PositionType", typeof(VisualPositionType), typeof(AssimpModelVisual3D),
                new PropertyMetadata(VisualPositionType.Center, OnScaleOrPositionChanged));
        #endregion

        #region SizeXProperty
        /// <summary>
        /// Gets or sets the size in of the 3D model in X dimension.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default value of SizeX is -1. This means that the original X Size of the object is preserved.
        /// </para>
        /// <para>
        /// How the SizeX is applied to the shown model depends on the <see cref="PreserveScaleAspectRatio"/> property.
        /// </para>
        /// <para>
        /// If it is false, than the x size of the shown object will be the same as the specified SizeX (if -1 than the original object x size is used).
        /// </para>
        /// <para>
        /// If PreserveScaleAspectRatio is true (by default), than the aspect ratio of the 3D model is preserved. This means that the model is stretched to one side mode than to the other - the scale is evenly set to all the axis. This also means that if all SizeX, SizeY and SizeZ are defined, the object will be scaled so it will not exceed and of the specified sizes.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>
        /// The following example shown a 3D model from ab3d.obj file. The model's center is positioned at (-250, 0, 0) and its size is set so its SizeX is 80. Note that by default the PreserveScaleAspectRatio is set to true.
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="80"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// <para>
        /// The following code shows the same mode, but this time it is custom sized:
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="50" SizeY="30" SizeZ="40"
        ///                    PreserveScaleAspectRatio="False"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// </example>
        public double SizeX
        {
            get { return (double)GetValue(SizeXProperty); }
            set { SetValue(SizeXProperty, value); }
        }

        /// <summary>
        /// SizeXProperty
        /// </summary>
        public static readonly DependencyProperty SizeXProperty =
            DependencyProperty.Register("SizeX", typeof(double), typeof(AssimpModelVisual3D),
                new PropertyMetadata(-1.0, OnScaleOrPositionChanged));
        #endregion

        #region SizeYProperty
        /// <summary>
        /// Gets or sets the size in of the 3D model in Y dimension.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default value of SizeY is -1. This means that the original Y Size of the object is preserved.
        /// </para>
        /// <para>
        /// How the SizeY is applied to the shown model depends on the <see cref="PreserveScaleAspectRatio"/> property.
        /// </para>
        /// <para>
        /// If it is false, than the y size of the shown object will be the same as the specified SizeY (if -1 than the original object y size is used).
        /// </para>
        /// <para>
        /// If PreserveScaleAspectRatio is true (by default), than the aspect ratio of the 3D model is preserved. This means that the model is stretched to one side mode than to the other - the scale is evenly set to all the axis. This also means that if all SizeX, SizeY and SizeZ are defined, the object will be scaled so it will not exceed and of the specified sizes.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>
        /// The following example shown a 3D model from ab3d.obj file. The model's center is positioned at (-250, 0, 0) and its size is set so its SizeX is 80. Note that by default the PreserveScaleAspectRatio is set to true.
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="80"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// <para>
        /// The following code shows the same mode, but this time it is custom sized:
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="50" SizeY="30" SizeZ="40"
        ///                    PreserveScaleAspectRatio="False"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// </example>
        public double SizeY
        {
            get { return (double)GetValue(SizeYProperty); }
            set { SetValue(SizeYProperty, value); }
        }

        /// <summary>
        /// SizeYProperty
        /// </summary>
        public static readonly DependencyProperty SizeYProperty =
            DependencyProperty.Register("SizeY", typeof(double), typeof(AssimpModelVisual3D),
                new PropertyMetadata(-1.0, OnScaleOrPositionChanged));
        #endregion

        #region SizeZProperty
        /// <summary>
        /// Gets or sets the size in of the 3D model in Z dimension.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default value of SizeZ is -1. This means that the original Z Size of the object is preserved.
        /// </para>
        /// <para>
        /// How the SizeZ is applied to the shown model depends on the <see cref="PreserveScaleAspectRatio"/> property.
        /// </para>
        /// <para>
        /// If it is false, than the z size of the shown object will be the same as the specified SizeZ (if -1 than the original object z size is used).
        /// </para>
        /// <para>
        /// If PreserveScaleAspectRatio is true (by default), than the aspect ratio of the 3D model is preserved. This means that the model is stretched to one side mode than to the other - the scale is evenly set to all the axis. This also means that if all SizeX, SizeY and SizeZ are defined, the object will be scaled so it will not exceed and of the specified sizes.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>
        /// The following example shown a 3D model from ab3d.obj file. The model's center is positioned at (-250, 0, 0) and its size is set so its SizeX is 80. Note that by default the PreserveScaleAspectRatio is set to true.
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="80"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// <para>
        /// The following code shows the same mode, but this time it is custom sized:
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="50" SizeY="30" SizeZ="40"
        ///                    PreserveScaleAspectRatio="False"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// </example>
        public double SizeZ
        {
            get { return (double)GetValue(SizeZProperty); }
            set { SetValue(SizeZProperty, value); }
        }

        /// <summary>
        /// SizeZProperty
        /// </summary>
        public static readonly DependencyProperty SizeZProperty =
            DependencyProperty.Register("SizeZ", typeof(double), typeof(AssimpModelVisual3D),
                new PropertyMetadata(-1.0, OnScaleOrPositionChanged));
        #endregion

        #region PreserveScaleAspectRatioProperty
        /// <summary>
        /// Gets or sets a Boolean that specifies if the 3D model is scaled so its aspect ratio is preserved (the ratio between width, height and depth of the object).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The size of the shown object is controlled by the <see cref="SizeX"/>, <see cref="SizeY"/>, <see cref="SizeZ"/> and <see cref="PreserveScaleAspectRatio"/> properties.
        /// </para>
        /// <para>
        /// By default all the SizeX, SizeY and SizeZ are set to -1. This means that the original size of the object is used. But if the SizeX is set to let's say 100, the object would be scaled so its SizeX would be 100.
        /// </para>
        /// <para>
        /// If PreserveScaleAspectRatio is true (default), than the aspect ratio of the 3D model is preserved. This means that the model is stretched to one side mode than to the other - the scale is evenly set to all the axis. This also means that if all SizeX, SizeY and SizeZ are defined, the object will be scaled so it will not exceed and of the specified sizes. 
        /// </para>
        /// <para>
        /// If PreserveScaleAspectRatio is false, than the aspect ration of the 3D model will not be preserved. In this case the SizeX, SizeY and SizeZ will be applied so the object will be exactly the size of the specified sizes.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>
        /// The following example shown a 3D model from ab3d.obj file. The model's center is positioned at (-250, 0, 0) and its size is set so its SizeX is 80. Note that by default the PreserveScaleAspectRatio is set to true.
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="80"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// <para>
        /// The following code shows the same mode, but this time it is custom sized:
        /// </para>
        /// <code lang="xaml">
        /// &lt;AssimpModelVisual3D:AssimpModelVisual3D Source="/Resources/ab3d.obj" 
        ///                    SizeX="50" SizeY="30" SizeZ="40"
        ///                    PreserveScaleAspectRatio="False"
        ///                    Position="0 40 0" PositionType="Center"/&gt;
        /// </code>
        /// </example>        
        public bool PreserveScaleAspectRatio
        {
            get { return (bool)GetValue(PreserveScaleAspectRatioProperty); }
            set { SetValue(PreserveScaleAspectRatioProperty, value); }
        }

        /// <summary>
        /// PreserveScaleAspectRatioProperty
        /// </summary>
        public static readonly DependencyProperty PreserveScaleAspectRatioProperty =
            DependencyProperty.Register("PreserveScaleAspectRatio", typeof(bool), typeof(AssimpModelVisual3D),
                new PropertyMetadata(true, OnScaleOrPositionChanged));
        #endregion

        #region DefaultMaterialProperty
        /// <summary>
        /// DefaultMaterialProperty
        /// </summary>
        public static readonly DependencyProperty DefaultMaterialProperty =
            DependencyProperty.Register("DefaultMaterial", typeof(Material), typeof(AssimpModelVisual3D),
            new PropertyMetadata(new DiffuseMaterial(Brushes.Silver), MaterialPropertyChanged));

        /// <summary>
        /// Gets or sets the default material that is used when the material is not defined in obj file. The property must be set before the file is read.
        /// </summary>
        public Material DefaultMaterial
        {
            set { SetValue(DefaultMaterialProperty, value); }
            get { return (Material)GetValue(DefaultMaterialProperty); }
        }

        static void MaterialPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var assimpModelVisual3D = (AssimpModelVisual3D) obj;

            if (assimpModelVisual3D._rootModel3D != null)
            {
                // If model was already read, then we can change old default material with new on
                var oldMaterial = (Material) e.OldValue;
                var newMaterial = (Material) e.NewValue;
                ChangeMaterial(assimpModelVisual3D._rootModel3D, newMaterial, oldMaterial);
            }
        }
        #endregion

        #region BeforeReadingFile
        /// <summary>
        /// BeforeReadingFile event occurs after the UsedAssimpWpfImporter has been created and before the actual file has been read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The BeforeReadingFile is useful if you want to set some special properties on the <see cref="Ab3d.Assimp.AssimpWpfImporter"/> object before it reads the file.
        /// </para>
        /// </remarks>
        public event EventHandler BeforeReadingFile;
        #endregion

        #region BaseUri
        Uri IUriContext.BaseUri
        {
            get
            {
                return (Uri)base.GetValue(BaseUriHelper.BaseUriProperty);
            }
            set
            {
                base.SetValue(BaseUriHelper.BaseUriProperty, value);
            }
        }
        #endregion

        /// <summary>
        /// Forces a reload of the model file
        /// </summary>
        public void Reload()
        {
            // Recreate the stream because the previous stream was probably closed
            if (this.Source != null)
            {
                ReloadFile(true); // true: forceReload
                CreateModel(); // This will also call ReloadFile, but it is called with false, so the code in the method is mostly skipped
            }
            else
            {
                SetModel(null);
            }
        }

        /// <summary>
        /// Creates this Model3D
        /// </summary>
        protected void CreateModel()
        {
            if (this.Source != null)
            {
                ReloadFile(false); // false: forceReload
                ResetModel();
            }
            else
            {
                SetModel(null);
            }
        }

        private void ResetModel()
        {
            if (!_isModelRead || _rootModel3D == null)
            {
                SetModel(null);
                return;
            }

            _originalObjectBounds = _rootModel3D.Bounds;

            UpdateScaleAndCenter(_rootModel3D);

            Model3D usedModel = _rootModel3D;

            // Add a new Model3DGroup so we do not apply transformation to the original object
            // Also if objects are cloned we add Model3DGroup so the created object has the same structure as the non-cloned one
            var newModelGroup = new Model3DGroup();
            newModelGroup.Children.Add(usedModel);

            newModelGroup.Transform = modelTransformGroup;

            SetModel(newModelGroup);
        }

        private void ReloadFile(bool forceReload)
        {
            // We cannot load native libraries into designer process - so no design time support
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
                return;

            if (_isSourceDirty || forceReload)
            { 
                // if it does not exist yet, create a new one, else just use the current instance and read another file (the current one) with it
                if (UsedAssimpWpfImporter == null)
                    UsedAssimpWpfImporter = new AssimpWpfImporter();

                var fileStream = GetStreamFromStringOrUrl(this.Source, uiElement: this, throwException: true, folderPath: out _folderPath);

                if (fileStream != null)
                {
                    // We need file extension as a hint to Assimp importer so it will select the correct importer
                    string fileExtension = System.IO.Path.GetExtension(this.Source.ToString());

                    UsedAssimpWpfImporter.DefaultMaterial = this.DefaultMaterial;

                    OnBeforeReadingFile();

                    using (fileStream)
                    {
                        // Read model
                        _rootModel3D = UsedAssimpWpfImporter.ReadModel3D(fileStream, fileExtension, GetResourceStream);
                    }
                }
                else
                {
                    _rootModel3D = null;
                }
            }

            _isModelRead = true;
            _isSourceDirty = false;
        }

        private Stream GetResourceStream(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
                return null;

            string finalFileName = null;

            if (!string.IsNullOrEmpty(this.TexturesPath))
                finalFileName = System.IO.Path.Combine(this.TexturesPath, resourceName);
            else if (!string.IsNullOrEmpty(_folderPath))
                finalFileName = System.IO.Path.Combine(_folderPath, resourceName);
            else
                finalFileName = resourceName;

            if (finalFileName == null)
                return null;

            string folderPath;
            var stream = GetStreamFromStringOrUrl(source: new Uri(finalFileName, UriKind.RelativeOrAbsolute), 
                                                  uiElement: this, 
                                                  throwException: false, 
                                                  folderPath: out folderPath);

            return stream;
        }

        private void UpdateScaleAndCenter()
        {
            if (_isModelRead && this.Content != null)
                UpdateScaleAndCenter(this.Content);
        }

        /// <summary>
        /// SetModelTranslate sets the modelTranslate as TranslateTransform3D. It is used to position the model. The method can be overridden to provide custom positioning.
        /// </summary>
        protected virtual void SetModelTranslate()
        {
            double dx, dy, dz;
            Point3D modelCenter;

            modelCenter = GetCenterPosition(_originalObjectBounds);

            dx = this.Position.X - modelCenter.X;
            dz = this.Position.Z - modelCenter.Z;

            if (this.PositionType == VisualPositionType.BottomCenter)
                dy = this.Position.Y - _originalObjectBounds.Y;
            else // this.PositionType == Ab3d.Common.Visuals.VisualPositionType.Center
                dy = this.Position.Y - modelCenter.Y;
            
            if (this.modelTranslate.OffsetX != dx)
                this.modelTranslate.OffsetX = dx;

            if (this.modelTranslate.OffsetY != dy)
                this.modelTranslate.OffsetY = dy;

            if (this.modelTranslate.OffsetZ != dz)
                this.modelTranslate.OffsetZ = dz;
        }

        /// <summary>
        /// Sets the Content of the Visual3D
        /// </summary>
        /// <param name="model">new Model3D</param>
        protected void SetModel(Model3D model)
        {
            if (model != null)
                Content = model;
            else
                Content = new GeometryModel3D();
        }

        /// <summary>
        /// SetModelScale set the modelScale as ScaleTransform3D. It is used to scale the model. The method can be overridden to provide custom scaling.
        /// </summary>
        protected virtual void SetModelScale()
        {
            double minScale;
            double scaleX, scaleY, scaleZ;
            bool preserveScaleX, preserveScaleY, preserveScaleZ;

            // Scale:
            if (!_originalObjectBounds.IsEmpty)
            {
                // Check which 
                preserveScaleX = this.SizeX < 0;
                preserveScaleY = this.SizeY < 0;
                preserveScaleZ = this.SizeZ < 0;


                if (preserveScaleX && preserveScaleY && preserveScaleZ)
                {
                    // if all scale values are Nan than preserve the original scale
                    if (modelScale.ScaleX != 1)
                        modelScale.ScaleX = 1;

                    if (modelScale.ScaleY != 1)
                        modelScale.ScaleY = 1;

                    if (modelScale.ScaleZ != 1)
                        modelScale.ScaleZ = 1;
                }
                else
                {
                    minScale = double.MaxValue; // used if PreserveScaleAspectRatio is true

                    if (!preserveScaleX)
                    {
                        scaleX = this.SizeX / _originalObjectBounds.SizeX;
                        minScale = Math.Min(minScale, scaleX);
                    }
                    else
                    {
                        scaleX = -1;
                    }


                    if (!preserveScaleY)
                    {
                        scaleY = this.SizeY / _originalObjectBounds.SizeY;
                        minScale = Math.Min(minScale, scaleY);
                    }
                    else
                    {
                        scaleY = -1;
                    }


                    if (!preserveScaleZ)
                    {
                        scaleZ = this.SizeZ / _originalObjectBounds.SizeZ;
                        minScale = Math.Min(minScale, scaleZ);
                    }
                    else
                    {
                        scaleZ = -1;
                    }


                    if (this.PreserveScaleAspectRatio)
                    {
                        scaleX = scaleY = scaleZ = minScale; // Get min scale - so the object is inside the this.Size
                    }
                    else
                    {
                        if (preserveScaleX)
                            scaleX = 1.0; // no scale on x axis

                        if (preserveScaleY)
                            scaleY = 1.0; // no scale on y axis

                        if (preserveScaleZ)
                            scaleZ = 1.0; // no scale on z axis
                    }

                    // Set rootScale
                    if (modelScale.ScaleX != scaleX)
                        modelScale.ScaleX = scaleX;

                    if (modelScale.ScaleY != scaleY)
                        modelScale.ScaleY = scaleY;

                    if (modelScale.ScaleZ != scaleZ)
                        modelScale.ScaleZ = scaleZ;

                    // Set scale center to this.CenterPosition
                    if (modelScale.CenterX != this.Position.X)
                        modelScale.CenterX = this.Position.X;

                    if (modelScale.CenterY != this.Position.Y)
                        modelScale.CenterY = this.Position.Y;

                    if (modelScale.CenterZ != this.Position.Z)
                        modelScale.CenterZ = this.Position.Z;
                }
            }
        }

        /// <summary>
        /// OnBeforeReadingFile
        /// </summary>
        protected void OnBeforeReadingFile()
        {
            if (BeforeReadingFile != null)
                BeforeReadingFile(this, null);
        }

        private void UpdateScaleAndCenter(Model3D model)
        {
            if (model == null)
                return;

            SetModelTranslate();

            SetModelScale();
        }

        private Point3D GetCenterPosition(Rect3D bounds)
        {
            Point3D center;

            center = new Point3D();

            center.X = bounds.X + bounds.SizeX / 2;
            center.Y = bounds.Y + bounds.SizeY / 2;
            center.Z = bounds.Z + bounds.SizeZ / 2;

            return center;
        }

        // From ReaderObj
        private static Stream GetStreamFromStringOrUrl(object source, DependencyObject uiElement, bool throwException, out string folderPath)
        {
            Stream uriStream = null;
            Uri originalUri;
            Uri baseUri = null;
            Uri mainUri;

            folderPath = null;

            string AuthoritySuffix = ":,,,";
            string SiteOfOriginAuthority = "siteOfOrigin" + AuthoritySuffix;
            string ApplicationAuthority = "application" + AuthoritySuffix;


            if (source is string)
                originalUri = new Uri((string)source, UriKind.RelativeOrAbsolute);
            else if (source is Uri)
            {
                originalUri = (Uri)source;

                if (originalUri.OriginalString == null || originalUri.OriginalString == "")
                {
                    return null;
                }
            }
            else
            {
                if (throwException)
                    throw new Exception("Source must be string or Uri!");
                else
                    return null;
            }

            if (!originalUri.IsAbsoluteUri && (uiElement != null))
                baseUri = System.Windows.Navigation.BaseUriHelper.GetBaseUri(uiElement);

            if (baseUri != null)
                mainUri = new Uri(baseUri, originalUri);
            else
                mainUri = originalUri;

            if (!mainUri.IsAbsoluteUri)
            {
                if (throwException)
                    throw new Exception("Cannot get Absolute Uri from Source!");
                else
                    return null;
            }

            if (mainUri.IsFile || mainUri.IsUnc)
            {
                folderPath = System.IO.Path.GetDirectoryName(mainUri.LocalPath);
                uriStream = new FileStream(mainUri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
#if !NET6_0_OR_GREATER
            else if (mainUri.Scheme == Uri.UriSchemeHttp || mainUri.Scheme == Uri.UriSchemeHttps)
            {
                WebRequest request = null;

                request = WebRequest.Create(mainUri);

            if (request != null)
                    uriStream = request.GetResponse().GetResponseStream();
                else
                {
                    if (throwException)
                        throw new Exception("Cannot create WebRequest from Uri!");
                    else
                        return null;
                }
            }
#endif
            else
            {
                System.Windows.Resources.StreamResourceInfo resourceInfo = null;

                if (Application.Current != null) // in case application was already disposed
                {
                    if (string.Compare(mainUri.Authority, SiteOfOriginAuthority, true) == 0)
                    {
                        resourceInfo = Application.GetRemoteStream(mainUri);

                        if (resourceInfo != null && !string.IsNullOrEmpty(mainUri.LocalPath))
                            folderPath = "pack://siteOfOrigin:,,,/" + GetResourcePathFolder(mainUri.LocalPath); // Convert '/Resources/ObjFiles/robotarm.obj' => '/Resources/ObjFiles/'
                    }
                    else if (string.Compare(mainUri.Authority, ApplicationAuthority, true) == 0)
                    {
                        // Check if it is a content and than if it is a resource
                        resourceInfo = Application.GetContentStream(mainUri);

                        //if (resourceInfo != null)
                        //{
                        //    if (!string.IsNullOrEmpty(mainUri.LocalPath))
                        //    {
                        //        string localPath = mainUri.LocalPath.Replace('/', '\\');

                        //        if (localPath[0] == '\\')
                        //            localPath = localPath.Substring(1);

                        //        folderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, localPath);
                        //        folderPath = System.IO.Path.GetDirectoryName(folderPath);
                        //    }
                        //}
                        //else
                        //{
                        //    resourceInfo = Application.GetResourceStream(mainUri);
                        //}

                        if (resourceInfo == null)
                            resourceInfo = Application.GetResourceStream(mainUri);

                        if (resourceInfo != null && !string.IsNullOrEmpty(mainUri.LocalPath)) // Convert '/Resources/ObjFiles/robotarm.obj' => '/Resources/ObjFiles/'
                            folderPath = "pack://application:,,,/" + GetResourcePathFolder(mainUri.LocalPath);
                    }
                    else
                    {
                    }
                }
                else
                {
                    // Application already disposed (for example if licensing dialog was opened and the application was closed before the dialog - after the dialog is closed the App.Current is already disposed
                    resourceInfo = null;
                    throwException = false; // do not throw exceptions
                }

                if (resourceInfo != null)
                    uriStream = resourceInfo.Stream;
                else
                {
#if !NET6_0_OR_GREATER
                    // As a last option try to create a WebRequest
                    // This also works for Design time support - there a Uri is in form: projectresource://-903803344/633054534540000438?file:///C:/Wpf/SVG/my svg_tests/my_flower.svg
                    try
                    {
                        WebRequest request = WebRequest.Create(mainUri);

                        if (request != null)
                        {

                            using (WebResponse response = request.GetResponse())
                            {
                                if (response.ContentLength > 0)
                                {
                                    byte[] byteContent = new byte[response.ContentLength];

                                    using (System.IO.Stream stream = response.GetResponseStream())
                                    {
                                        for (int i = 0; i < Convert.ToInt32(response.ContentLength); i++)
                                            byteContent[i] = Convert.ToByte(stream.ReadByte());

                                        // Sometimes just fills the last part of the array with '\0' - see http://www.bsi.si/_data/tecajnice/dtecbs.xml
                                        //stream.Read(byteContent, 0, Convert.ToInt32(HttpWResp.ContentLength));
                                    }

                                    uriStream = new MemoryStream(byteContent);
                                }
                            }
                        }

                    }
                    catch
                    { }
#endif
                }

                if (uriStream == null && throwException)
                    throw new Exception("Cannot resolve URI's Host!");
            }

            return uriStream;
        }

        // Convert '/Resources/ObjFiles/robotarm.obj' => 'Resources/ObjFiles/'
        // Also always remove leading '/'
        // And ensures that the string is completed with '/'
        private static string GetResourcePathFolder(string fullLocalPath)
        {
            if (string.IsNullOrEmpty(fullLocalPath))
                return null;

            int pos1;
            if (fullLocalPath[0] == '/')
                pos1 = 1;
            else
                pos1 = 0;

            int pos2 = fullLocalPath.LastIndexOf('/');

            string pathFolder;

            if (pos2 == -1)
                pathFolder = ""; // No folders
            else
                pathFolder = fullLocalPath.Substring(pos1, pos2 - pos1 + 1);

            return pathFolder;
        }


        // FROM Ab3d.PowerToys/Visuals/BaseVisual3D.cs

#region OnPropertyChanged, UpdateContentIfNotInizializing, CreateModel
        /// <summary>
        /// OnPropertyChanged method calls UpdateContentIfNotInitializing that recreates the model is visible (IsVisible is true) and if not initializing (between BeginInit and EndInit).
        /// </summary>
        /// <param name="obj">DependencyObject</param>
        /// <param name="args">DependencyPropertyChangedEventArgs</param>
        protected static void OnPropertyChanged(DependencyObject obj,
                                                DependencyPropertyChangedEventArgs args)
        {
            ((AssimpModelVisual3D)obj).UpdateContentIfNotInitializing();
        }

        /// <summary>
        /// Recreates the models if the Visual3D is visible (IsVisible is true) and it is not initializing (between BeginInit and EndInit)
        /// </summary>
        protected virtual void UpdateContentIfNotInitializing()
        {
            if (!isInitializing)
            {
                if (IsVisible)
                {
                    // if content is visible than recreate the model
                    CreateModel();
                }
                else
                {
                    // if content is not visible, than just clear the saved model
                    // This will create a new model when the IsVisible will be set back to true
                    savedHiddenContent = null;
                }
            }
        }
        
        /// <summary>
        /// OnVisualParentChanged
        /// </summary>
        /// <param name="oldParent">oldParent</param>
        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            // If no property is changed (for example if default size is used) than the geometry is not created (CreateModel is not called)
            // Here we make sure that the CreateModel is called at least when the visual is added to the parent
            if (Content == null || (Content is GeometryModel3D && ((GeometryModel3D)Content).Geometry == null))
                CreateModel();

            base.OnVisualParentChanged(oldParent);
        }
#endregion

#region Common property validators (ValidateDoublePropertyValue, ValidatePositiveDoublePropertyValue, ...)
        /// <summary>
        /// Returns true if value is valid double
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>true if value is valid double</returns>
        protected static bool ValidateDoublePropertyValue(object value)
        {
            return (IsValidDouble((double)value));
        }

        /// <summary>
        /// Returns true if value is valid double and is positive
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>true if value is valid double and is positive</returns>
        protected static bool ValidatePositiveDoublePropertyValue(object value)
        {
            return (IsValidDouble((double)value) && (double)value > 0);
        }

        /// <summary>
        /// Returns true if value is positive integer
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>true if value is positive integer</returns>
        protected static bool ValidatePositiveIntPropertyValue(object value)
        {
            return (int)value > 0;
        }

        /// <summary>
        /// Returns true if the value is a valid Size3D object
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>true if the value is a valid Size3D object</returns>
        protected static bool ValidateSize3DPropertyValue(object value)
        {
            Size3D size;

            size = (Size3D)value;

            return IsValidSize3D(size);
        }

        /// <summary>
        /// Returns true if the value is a valid Size object
        /// </summary>
        /// <param name="value">value</param>
        /// <returns>true if the value is a valid Size object</returns>
        protected static bool ValidateSizePropertyValue(object value)
        {
            Size size;

            size = (Size)value;

            return IsValidSize(size);
        }

        private static bool IsValidSize3D(Size3D size)
        {
            return (IsValidAndPositiveDouble(size.X) &&
                    IsValidAndPositiveDouble(size.Y) &&
                    IsValidAndPositiveDouble(size.Z));
        }

        private static bool IsValidDouble(double value)
        {
            return !(double.IsNaN(value) || double.IsInfinity(value));
        }

        private static bool IsValidAndPositiveDouble(double value)
        {
            return (!(double.IsNaN(value) || double.IsInfinity(value)) && ((double)value) > 0.0);
        }

        private static bool IsValidSize(Size size)
        {
            return (IsValidAndPositiveDouble(size.Width) &&
                    IsValidAndPositiveDouble(size.Height));
        }
#endregion

#region ISupportInitialize Members

        /// <summary>
        /// if true the Visual3D is initializing (between BeginInit and EndInit)
        /// </summary>
        protected bool isInitializing;

        /// <summary>
        /// Signals the line 3D that initialization is starting.
        /// </summary>
        public void BeginInit()
        {
            isInitializing = true;
        }

        /// <summary>
        /// Signals the line 3D that initialization is complete.
        /// </summary>
        public void EndInit()
        {
            isInitializing = false;
            UpdateContentIfNotInitializing();
        }

#endregion

        // FROM Ab3d.PowerToys/Utilities/ModelUtils.cs
        private static void ChangeMaterial(Model3D model, Material newMaterial, Material newBackMaterial)
        {
            if (model is Model3DGroup)
            {
                var originalModel3DGroup = (Model3DGroup)model;

                foreach (Model3D childModel3D in originalModel3DGroup.Children)
                    ChangeMaterial(childModel3D, newMaterial, newBackMaterial);
            }
            else if (model is GeometryModel3D)
            {
                var originalGeometryModel3D = (GeometryModel3D)model;
                originalGeometryModel3D.Material = newMaterial;
                originalGeometryModel3D.BackMaterial = newBackMaterial;
            }
        }

    }
}
