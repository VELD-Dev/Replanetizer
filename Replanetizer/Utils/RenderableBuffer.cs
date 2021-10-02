using LibReplanetizer;
using LibReplanetizer.LevelObjects;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using Replanetizer.Utils;
using System.Drawing;

namespace Replanetizer.Utils
{
    /*
     * A container to store IBO and VBO references for a Model
     */
    public class RenderableBuffer
    {
        private ModelObject modelObject;

        private int ibo = 0;
        private int vbo = 0;

        public int ID { get; }
        public RenderedObjectType type { get; }
        public int light { get; set; }
        public Color ambient { get; set; }

        private bool selected;

        private Level level;
        private Dictionary<Texture, int> textureIds;

        public static int matrixID { get; set; }
        public static int shaderID { get; set; }
        public static int colorShaderID { get; set; }
        public static Matrix4 worldView { get; set; }

        public RenderableBuffer(ModelObject modelObject, RenderedObjectType type, int ID, Level level, Dictionary<Texture, int> textureIds)
        {
            this.modelObject = modelObject;
            this.ID = ID;
            this.type = type;
            this.textureIds = textureIds;
            this.level = level;

            BufferUsageHint hint = BufferUsageHint.StaticDraw;
            if (modelObject.IsDynamic())
            {
                hint = BufferUsageHint.DynamicDraw;
            }

            // IBO
            int iboLength = modelObject.GetIndices().Length * sizeof(ushort);
            if (iboLength > 0)
            {
                GL.GenBuffers(1, out ibo);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, iboLength * sizeof(ushort), IntPtr.Zero, hint);
            }

            // VBO
            int vboLength = modelObject.GetVertices().Length * sizeof(float);
            if (type == RenderedObjectType.Terrain) vboLength += modelObject.model.rgbas.Length * sizeof(Byte);
            if (vboLength > 0)
            {
                GL.GenBuffers(1, out vbo);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vboLength, IntPtr.Zero, hint);
            }

            UpdateBuffers();
            UpdateUniforms();
        }

        /// <summary>
        /// Updates the buffers. This is not actually needed as long as mesh manipulations are not possible.
        /// </summary>
        public void UpdateBuffers()
        {
            if (BindIBO())
            {
                ushort[] iboData = modelObject.GetIndices();
                GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, iboData.Length * sizeof(ushort), iboData);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            }

            if (BindVBO())
            {
                float[] vboData = modelObject.GetVertices();
                if (type == RenderedObjectType.Terrain)
                {
                    byte[] rgbas = modelObject.model.rgbas;
                    float[] fullData = new float[vboData.Length + rgbas.Length / 4];
                    for (int i = 0; i < vboData.Length / 8; i++)
                    {
                        fullData[9 * i + 0] = vboData[8 * i + 0];
                        fullData[9 * i + 1] = vboData[8 * i + 1];
                        fullData[9 * i + 2] = vboData[8 * i + 2];
                        fullData[9 * i + 3] = vboData[8 * i + 3];
                        fullData[9 * i + 4] = vboData[8 * i + 4];
                        fullData[9 * i + 5] = vboData[8 * i + 5];
                        fullData[9 * i + 6] = vboData[8 * i + 6];
                        fullData[9 * i + 7] = vboData[8 * i + 7];
                        fullData[9 * i + 8] = BitConverter.ToSingle(rgbas, i * 4);
                    }
                    vboData = fullData;
                }
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vboData.Length * sizeof(float), vboData);
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            }
        }

        /// <summary>
        /// Updates the light and ambient variables which can then be used to update the shader.
        /// </summary>
        public void UpdateUniforms()
        {
            switch (type)
            {
                case RenderedObjectType.Moby:
                    Moby mob = (Moby) modelObject;
                    light = mob.light;
                    ambient = mob.color;
                    break;
                case RenderedObjectType.Tie:
                    Tie tie = (Tie) modelObject;
                    light = tie.light;
                    break;
                case RenderedObjectType.Shrub:
                    Shrub shrub = (Shrub) modelObject;
                    light = shrub.light;
                    ambient = shrub.color;
                    break;
            }
        }

        /// <summary>
        /// Sets an internal variable to true if the corresponding modelObject is equal to
        /// the selectedObject in which case an outline will be rendered.
        /// </summary>
        public void Select(LevelObject selectedObject)
        {
            selected = modelObject == selectedObject;
        }

        /// <summary>
        /// Renders an object based on the buffers.
        /// </summary>
        public void Render(bool switchBlends)
        {
            if (!BindIBO() || !BindVBO()) return;
            Matrix4 mvp = modelObject.modelMatrix * worldView;  //Has to be done in this order to work correctly
            GL.UniformMatrix4(matrixID, false, ref mvp);

            SetupVertexAttribPointers();

            //Bind textures one by one, applying it to the relevant vertices based on the index array
            foreach (TextureConfig conf in modelObject.model.textureConfig)
            {
                GL.BindTexture(TextureTarget.Texture2D, (conf.ID > 0) ? textureIds[level.textures[conf.ID]] : 0);
                GL.DrawElements(PrimitiveType.Triangles, conf.size, DrawElementsType.UnsignedShort, conf.start * sizeof(ushort));
            }

            if (selected)
            {
                if (switchBlends)
                    GL.Disable(EnableCap.Blend);

                GL.UseProgram(colorShaderID);
                GL.UniformMatrix4(matrixID, false, ref mvp);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.DrawElements(PrimitiveType.Triangles, modelObject.model.indexBuffer.Length, DrawElementsType.UnsignedShort, 0);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.UseProgram(shaderID);

                if (switchBlends)
                    GL.Enable(EnableCap.Blend);
            }
        }

        /// <summary>
        /// Attempts to bind the index buffer object. Returns true on success, false else.
        /// </summary>
        public bool BindIBO()
        {
            bool success = ibo != 0;
            if (success)
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);

            return success;
        }

        /// <summary>
        /// Attempts to bind the vertex buffer object. Returns true on success, false else.
        /// </summary>
        public bool BindVBO()
        {
            bool success = vbo != 0;
            if (success)
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            return success;
        }

        private void SetupVertexAttribPointers()
        {
            if (type == RenderedObjectType.Terrain)
            {
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 9, 0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeof(float) * 9, sizeof(float) * 3);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, sizeof(float) * 9, sizeof(float) * 6);
                GL.VertexAttribPointer(3, 4, VertexAttribPointerType.UnsignedByte, true, sizeof(float) * 9, sizeof(float) * 8);
            }
            else
            {
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, 0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 3);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, sizeof(float) * 8, sizeof(float) * 6);
            }
        }
    }
}