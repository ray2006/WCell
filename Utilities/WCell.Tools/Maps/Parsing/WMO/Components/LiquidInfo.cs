using WCell.Util.Graphics;

namespace WCell.Tools.Maps.Parsing.WMO.Components
{
    /// <summary>
    /// MLIQ Info
    /// </summary>
    public class LiquidInfo
    {
        /// <summary>
        /// number of X vertices (xverts)
        /// </summary>
        public int XVertexCount;
        /// <summary>
        /// number of Y vertices (yverts)
        /// </summary>
        public int YVertexCount;
        /// <summary>
        /// number of X tiles (xtiles = xverts-1)
        /// </summary>
        public int XTileCount;
        /// <summary>
        /// number of Y tiles (ytiles = yverts-1)
        /// </summary>
        public int YTileCount;

        public Vector3 BaseCoordinates;
        public ushort MaterialId;

        public int VertexCount
        {
            get { return XVertexCount*YVertexCount; }
        }
        public int TileCount
        {
            get { return XTileCount*YTileCount; }
        }

        /// <summary>
        /// Grid of the Upper height bounds for the liquid vertices
        /// </summary>
        public float[,] HeightMapMax;

        /// <summary>
        /// Grid of the lower height bounds for the liquid vertices
        /// </summary>
        public float[,] HeightMapMin;

        /// <summary>
        /// Grid of flags for the liquid tiles
        /// They are often masked with 0xF
        /// They seem to determine the liquid type?
        /// </summary>
        public byte[,] LiquidTileFlags;

        public BoundingBox Bounds;

        /// <summary>
        /// The calculated LiquidType (for the DBC lookup) for the whole WMO
        /// Not parsed initially
        /// </summary>
        public uint LiquidType;
    }
}