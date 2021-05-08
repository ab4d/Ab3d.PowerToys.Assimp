using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Assimp;
using Vector3D = Assimp.Vector3D;

namespace Ab3d.Assimp
{
    /// <summary>
    /// AssimpExtensions contains extension methods that can convert Assimp structs to WPF structs.
    /// </summary>
    public static class AssimpExtensions
    {
        /// <summary>
        /// Converts Assimp Vector2D to WPF Point
        /// </summary>
        /// <param name="value">Vector2D</param>
        /// <returns>Point</returns>
        public static Point ToWpfPoint(this Vector2D value)
        {
            return new Point((double)value.X, (double)value.Y);
        }

        /// <summary>
        /// Converts Assimp Vector3D to WPF Point3D
        /// </summary>
        /// <param name="value">Assimp Vector3D</param>
        /// <returns>WPF Point3D</returns>
        public static System.Windows.Media.Media3D.Point3D ToWpfPoint3D(this Vector3D value)
        {
            return new System.Windows.Media.Media3D.Point3D((double)value.X, (double)value.Y, (double)value.Z);
        }

        /// <summary>
        /// Converts Assimp Vector3D to WPF Vector3D
        /// </summary>
        /// <param name="value">Assimp Vector3D</param>
        /// <returns>WPF Point3D</returns>
        public static System.Windows.Media.Media3D.Vector3D ToWpfVector3D(this Vector3D value)
        {
            return new System.Windows.Media.Media3D.Vector3D((double)value.X, (double)value.Y, (double)value.Z);
        }

        /// <summary>
        /// Converts Assimp Color4D to WPF Color
        /// </summary>
        /// <param name="value">Color4D</param>
        /// <returns>Color</returns>
        public static Color ToWpfColor(this Color4D value)
        {
            return Color.FromArgb((byte)(value.A * 255), (byte)(value.R * 255), (byte)(value.G * 255), (byte)(value.B * 255));
        }

        /// <summary>
        /// Converts Assimp Color4D to WPF Color
        /// </summary>
        /// <param name="value">Color4D</param>
        /// <param name="alpha">alpha</param>
        /// <returns>Color</returns>
        public static Color ToWpfColor(this Color4D value, float alpha)
        {
            return Color.FromArgb((byte)(alpha * 255), (byte)(value.R * 255), (byte)(value.G * 255), (byte)(value.B * 255));
        }

        /// <summary>
        /// Converts Assimp Matrix4x4 (row-major) to WPF Matrix3D (column-major)
        /// </summary>
        /// <param name="m">Matrix4x4</param>
        /// <returns>Matrix3D</returns>
        public static System.Windows.Media.Media3D.Matrix3D ToWpfMatrix3D(this Matrix4x4 m)
        {
            // Read the row-major data into column-major WPF matrix
            return new System.Windows.Media.Media3D.Matrix3D((double)m.A1, (double)m.B1, (double)m.C1, (double)m.D1,
                                                             (double)m.A2, (double)m.B2, (double)m.C2, (double)m.D2,
                                                             (double)m.A3, (double)m.B3, (double)m.C3, (double)m.D3,
                                                             (double)m.A4, (double)m.B4, (double)m.C4, (double)m.D4);
        }

        /// <summary>
        /// Converts WPF Color to Assimp Color4D
        /// </summary>
        /// <param name="value">WPF Color</param>
        /// <returns>Assimp Color4D</returns>
        public static Color4D ToAssimpColor(this Color value)
        {
            return new Color4D(((float)value.R) / 255.0f, ((float)value.G) / 255.0f, ((float)value.B) / 255.0f, ((float)value.A) / 255.0f);
        }

        /// <summary>
        /// Converts WPF Point3D to Assimp Vector3D
        /// </summary>
        /// <param name="value">WPF Point3D</param>
        /// <returns>Assimp Vector3D</returns>
        public static Vector3D ToAssimpVector3D(this System.Windows.Media.Media3D.Point3D value)
        {
            return new Vector3D((float)value.X, (float)value.Y, (float)value.Z);
        }

        /// <summary>
        /// Converts WPF Vector3D to Assimp Vector3D
        /// </summary>
        /// <param name="value">WPF Vector3D</param>
        /// <returns>Assimp Vector3D</returns>
        public static Vector3D ToAssimpVector3D(this System.Windows.Media.Media3D.Vector3D value)
        {
            return new Vector3D((float)value.X, (float)value.Y, (float)value.Z);
        }

        /// <summary>
        /// Dumps the detailed information about Assimp Scene into the Visual Studio Output window
        /// </summary>
        /// <param name="assimpScene">assimpScene</param>
        /// <param name="dumpMeshes">dumpMeshes</param>
        /// <param name="dumpSceneNodes">dumpSceneNodes</param>
        /// <param name="dumpMaterials">dumpMaterials</param>
        /// <param name="dumpCameras">dumpCameras</param>
        /// <param name="dumpLights">dumpLights</param>
        /// <param name="dumpTransformations">dumpTransformations</param>
        /// <param name="dumpAnimations">dumpAnimations</param>
        /// <param name="dumpBones">dumpBones</param>
        public static void Dump(this Scene assimpScene, bool dumpMeshes = true, bool dumpSceneNodes = true, bool dumpMaterials = true, bool dumpCameras = true, bool dumpLights = true, bool dumpTransformations = true, bool dumpAnimations = true, bool dumpBones = true)
        {
            AssimpDumper.Dump(assimpScene, dumpMeshes, dumpSceneNodes, dumpMaterials, dumpCameras, dumpLights, dumpTransformations, dumpAnimations, dumpBones);
        }
    }
}