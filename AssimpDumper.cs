#define DEBUG

using System;
using System.Text;
using Assimp;

namespace Ab3d.Assimp
{
    /// <summary>
    /// Dumper class is a helper class that contains method that can show detailed information about Assimp Scene.
    /// </summary>
    public static class AssimpDumper
    {
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
        public static void Dump(Scene assimpScene, bool dumpMeshes = true, bool dumpSceneNodes = true, bool dumpMaterials = true, bool dumpCameras = true, bool dumpLights = true, bool dumpTransformations = true, bool dumpAnimations = true, bool dumpBones = true)
        {
            var dumpString = GetDumpString(assimpScene, dumpMeshes, dumpSceneNodes, dumpMaterials, dumpCameras, dumpLights, dumpTransformations, dumpAnimations, dumpBones);
            System.Diagnostics.Debug.WriteLine(dumpString);
        }

        /// <summary>
        /// Gets a string with detailed information about Assimp Scene.
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
        /// <returns>string with detailed information about Assimp Scene</returns>
        public static string GetDumpString(Scene assimpScene, bool dumpMeshes = true, bool dumpSceneNodes = true, bool dumpMaterials = true, bool dumpCameras = true, bool dumpLights = true, bool dumpTransformations = true, bool dumpAnimations = true, bool dumpBones = true)
        {
            var sb = new StringBuilder();

            if (assimpScene.SceneFlags != SceneFlags.None)
                sb.AppendFormat("SceneFlags: {0}\r\n", assimpScene.SceneFlags);

            if (dumpMeshes && assimpScene.HasMeshes)
            {
                sb.AppendFormat("Meshes (count: {0}):\r\n", assimpScene.Meshes.Count);

                for (var i = 0; i < assimpScene.Meshes.Count; i++)
                {
                    var mesh = assimpScene.Meshes[i];
                    sb.AppendFormat("    [{0}]: '{1}' Vertexes: {2}; Faces: {3}; PrimitiveType: {4}\r\n", i, mesh.Name ?? "<null>", mesh.VertexCount, mesh.FaceCount, mesh.PrimitiveType);
                    if (dumpBones && mesh.HasBones)
                    {
                        sb.AppendLine("           Bones: ");
                        foreach (var meshBone in mesh.Bones)
                        {
                            sb.AppendFormat("           '{0}' VertexWeights: {1}  ", meshBone.Name ?? "<null>", meshBone.VertexWeightCount);
                            if (dumpTransformations)
                                sb.Append("   OffsetMatrix: ").Append(GetMatrixStringInOneLine(meshBone.OffsetMatrix));

                            sb.AppendLine();
                        }
                    }
                }

                sb.AppendLine();
            }

            if (dumpSceneNodes && assimpScene.RootNode != null)
            {
                sb.AppendLine("SceneNodes:");
                AddSceneNodeString(sb, assimpScene.RootNode, "    ");
                sb.AppendLine();
            }

            if (dumpMaterials && assimpScene.HasMaterials || assimpScene.HasTextures)
            {
                if (assimpScene.HasTextures)
                {
                    sb.AppendLine("Textures:");
                    for (var i = 0; i < assimpScene.Textures.Count; i++)
                        sb.AppendFormat("    [{0}]: {1} x {2} = {3} bytes\r\n", i, assimpScene.Textures[i].Width, assimpScene.Textures[i].Height, assimpScene.Textures[i].CompressedDataSize);
                }

                if (assimpScene.HasMaterials)
                {
                    sb.AppendLine("Materials:");
                    foreach (var material in assimpScene.Materials)
                    {
                        sb.AppendFormat("    '{0}' Diffuse {1}; Opacity: {2}", material.Name ?? "<null>", material.ColorDiffuse, material.Opacity);
                        if (material.HasTextureDiffuse)
                        {
                            string textureFilePath;
                            TextureSlot textureSlot;
                            bool isDiffuseTexture = material.GetMaterialTexture(TextureType.Diffuse, 0, out textureSlot);

                            if (isDiffuseTexture)
                                textureFilePath = textureSlot.FilePath;
                            else
                                textureFilePath = null;

                            sb.AppendFormat("; TextureType: {0}; DiffuseTextureIndex: {1}; FilePath: {2}", material.TextureDiffuse.TextureType, material.TextureDiffuse.TextureIndex, textureFilePath ?? "<null>");
                        }

                        sb.AppendLine();
                    }
                }

                sb.AppendLine();
            }

            if (dumpAnimations && assimpScene.HasAnimations)
            {
                sb.AppendLine("Animations:");
                foreach (var sceneAnimation in assimpScene.Animations)
                {
                    sb.AppendFormat("    '{0}', DurationInTicks: {1}, TicksPerSecond: {2}\r\n", sceneAnimation.Name, sceneAnimation.DurationInTicks, sceneAnimation.TicksPerSecond);

                    if (sceneAnimation.HasMeshAnimations)
                    {
                        sb.AppendLine("    MeshAnimations:");

                        for (var i = 0; i < sceneAnimation.MeshAnimationChannels.Count; i++)
                        {
                            sb.AppendFormat("        [{0}]: '{1}' Keys count: {2}\r\n", i, sceneAnimation.MeshAnimationChannels[i].MeshName,
                                sceneAnimation.MeshAnimationChannels[i].HasMeshKeys ? sceneAnimation.MeshAnimationChannels[i].MeshKeys.Count : 0);
                        }
                    }

                    if (sceneAnimation.HasNodeAnimations)
                    {
                        sb.AppendLine("    NodeAnimations:");

                        for (var i = 0; i < sceneAnimation.NodeAnimationChannels.Count; i++)
                        {
                            sb.AppendFormat("        [{0}]: '{1}' PositionKeys: {2}, RotationKeys: {3}, ScalingKeys: {4}\r\n",
                                i,
                                sceneAnimation.NodeAnimationChannels[i].NodeName, 
                                sceneAnimation.NodeAnimationChannels[i].PositionKeyCount, 
                                sceneAnimation.NodeAnimationChannels[i].RotationKeyCount,
                                sceneAnimation.NodeAnimationChannels[i].ScalingKeyCount);
                        }
                    }

                    sb.AppendLine();
                }
            }

            if (dumpCameras && assimpScene.HasCameras)
            {
                sb.AppendLine("Cameras:");
                foreach (var assimpSceneCamera in assimpScene.Cameras)
                {
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "    '{0}' Position: {1}, Direction: {2}\r\n", assimpSceneCamera.Name ?? "<null>", assimpSceneCamera.Position, assimpSceneCamera.Direction);
                }

                sb.AppendLine();
            }

            if (dumpLights && assimpScene.HasLights)
            {
                sb.AppendLine("Lights:");
                foreach (var assimpSceneLight in assimpScene.Lights)
                {
                    sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "    '{0}' Type: {1}, Position: {2}\r\n", assimpSceneLight.Name ?? "<null>", assimpSceneLight.LightType, assimpSceneLight.Position);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AddSceneNodeString(StringBuilder sb, Node sceneNode, string indent = "", bool dumpTransformations = true)
        {
            sb.AppendFormat("{0}'{1}' ", indent, sceneNode.Name ?? "<null>");
            if (sceneNode.HasMeshes)
            {
                if (sceneNode.Meshes != null)
                    sb.AppendFormat(" Meshes: {{ {0} }}  ", string.Join(",", sceneNode.Meshes));

                if (sceneNode.MeshIndices != null)
                    sb.AppendFormat(" MeshIndices: {{ {0} }}  ", string.Join(",", sceneNode.MeshIndices));
            }

            if (sceneNode.Metadata != null && sceneNode.Metadata.Count > 0)
            {
                sb.Append("  Metadata: ");
                foreach (var keyValuePair in sceneNode.Metadata)
                    sb.AppendFormat("'{0}' = '{1}; ", keyValuePair.Key, keyValuePair.Value == null ? "<null>" : keyValuePair.Value.ToString());
            }

            if (dumpTransformations)
                sb.Append("  Transform: ").Append(GetMatrixStringInOneLine(sceneNode.Transform));

            sb.AppendLine();


            if (sceneNode.HasChildren)
            {
                foreach (var sceneNodeChild in sceneNode.Children)
                    AddSceneNodeString(sb, sceneNodeChild, indent + "    ", dumpTransformations);
            }
        }

        private static string GetMatrixStringInOneLine(Matrix4x4 matrix, int numberOfDecimals = 2, bool checkForIdentity = true)
        {
            if (checkForIdentity)
            {
                double epsilon = 0.000000001;
                if (Math.Abs(matrix.A1 - 1) < epsilon && Math.Abs(matrix.A2) < epsilon && Math.Abs(matrix.A3) < epsilon && Math.Abs(matrix.A4) < epsilon &&
                    Math.Abs(matrix.B1) < epsilon && Math.Abs(matrix.B2 - 1) < epsilon && Math.Abs(matrix.B3) < epsilon && Math.Abs(matrix.B4) < epsilon &&
                    Math.Abs(matrix.C1) < epsilon && Math.Abs(matrix.C2) < epsilon && Math.Abs(matrix.C3 - 1) < epsilon && Math.Abs(matrix.C4) < epsilon &&
                    Math.Abs(matrix.D1) < epsilon && Math.Abs(matrix.D2) < epsilon && Math.Abs(matrix.D3) < epsilon && Math.Abs(matrix.D4 - 1) < epsilon)
                {
                    return "Identity";
                }
            }

            if (numberOfDecimals <= 0)
                numberOfDecimals = 2;

            string decimalFormatString = 'F' + numberOfDecimals.ToString();

            return "[ [ " +
                    matrix.A1.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.B1.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.C1.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.D1.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " ] [ " +
                    
                    matrix.A2.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.B2.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.C2.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.D2.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " ] [ " +
                    
                    matrix.A3.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.B3.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.C3.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.D3.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " ] [ " +
                    
                    matrix.A4.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.B4.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.C4.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " " +
                    matrix.D4.ToString(decimalFormatString, System.Globalization.CultureInfo.InvariantCulture) + " ] ]";
        }
    }
}