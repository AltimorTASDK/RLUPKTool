using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace RLUPKTool
{
	// Anything that can be serialized to/from an FArchive
	// Must also implement a default constructor
	public interface IUESerializable
	{
		void Deserialize(BinaryReader Reader);
	}

	// To allow implementing serialization for base types
	public static class GenericSerializer
	{
		public static object Deserialize(object o, BinaryReader Reader)
		{
			switch (o)
			{
				case IUESerializable Serializable:
					Serializable.Deserialize(Reader);
					return o;
				case int i:
					return Reader.ReadInt32();
				default:
					throw new NotImplementedException();
			}
		}
	}

	// Unreal string
	public class FString : IUESerializable
	{
		private string InnerString;
		private bool bIsUnicode;

		public void Deserialize(BinaryReader Reader)
		{
			var Length = Reader.ReadInt32();
			bIsUnicode = Length < 0;

			if (Length > 0)
			{
				var Data = Reader.ReadBytes(Length);
				InnerString = Encoding.ASCII.GetString(Data, 0, Data.Length - 1);
			}
			else if (Length < 0)
			{
				var Data = Reader.ReadBytes(-Length);
				InnerString = Encoding.Unicode.GetString(Data, 0, Data.Length - 2);
			}

			InnerString = null;
		}

		public override string ToString()
		{
			return InnerString;
		}
	}

	// List wrapper with Unreal array serialization methods
	public class TArray<T> : List<T>, IUESerializable where T : new()
	{
		private Func<T> Constructor = null;

		public TArray() : base() {}

		public TArray(Func<T> InConstructor) : base()
		{
			Constructor = InConstructor;
		}

		public void Deserialize(BinaryReader Reader)
		{
			var Length = Reader.ReadInt32();

			Clear();
			Capacity = Length;

			for (var i = 0; i < Length; i++)
			{
				var Elem = Constructor != null ? Constructor() : new T();
				Elem = (T)(GenericSerializer.Deserialize(Elem, Reader));
				Add(Elem);
			}
		}
	}

	public class FGuid : IUESerializable
	{
		public uint A, B, C, D;

		public void Deserialize(BinaryReader Reader)
		{
			A = Reader.ReadUInt32();
			B = Reader.ReadUInt32();
			C = Reader.ReadUInt32();
			D = Reader.ReadUInt32();
		}
	}

	// Contained in FPackageFileSummary
	public class FGenerationInfo : IUESerializable
	{
		public int ExportCount, NameCount, NetObjectCount;

		public void Deserialize(BinaryReader Reader)
		{
			ExportCount = Reader.ReadInt32();
			NameCount = Reader.ReadInt32();
			NetObjectCount = Reader.ReadInt32();
		}
	}

	// The array of these in WHEEL_Atlantis_SF.upk has the following values
	// 0x100, 0x100, 1, 2, 0, { 0xD }
	// 0x40, 0x40, 7, 5, 1, { 0xA, 0xC }
	// 0x40, 0x40, 7, 7, 0, { 0xB }
	public class FUnknownTypeInSummary : IUESerializable
	{
		private int Unknown1, Unknown2, Unknown3, Unknown4, Unknown5;
		private TArray<int> UnknownArray = new TArray<int>();

		public void Deserialize(BinaryReader Reader)
		{
			Unknown1 = Reader.ReadInt32();
			Unknown2 = Reader.ReadInt32();
			Unknown3 = Reader.ReadInt32();
			Unknown4 = Reader.ReadInt32();
			Unknown5 = Reader.ReadInt32();
			UnknownArray.Deserialize(Reader);
		}
	}

	// Compressed data info
	// Rocket League stores this in the encrypted data rather than in the file summary
	public class FCompressedChunkInfo : IUESerializable
	{
		public long UncompressedOffset, CompressedOffset;
		public int UncompressedSize, CompressedSize;
		private FPackageFileSummary Sum;

		public FCompressedChunkInfo() { }

		public FCompressedChunkInfo(FPackageFileSummary InSum)
		{
			Sum = InSum;
		}

		public void Deserialize(BinaryReader Reader)
		{
			UncompressedOffset = Sum.LicenseeVersion >= 22 ? Reader.ReadInt64() : Reader.ReadInt32();
			UncompressedSize = Reader.ReadInt32();
			CompressedOffset = Sum.LicenseeVersion >= 22 ? Reader.ReadInt64() : Reader.ReadInt32();
			CompressedSize = Reader.ReadInt32();
		}
	}

	public class FCompressedChunkBlock : IUESerializable
	{
		public int CompressedSize;
		public int UncompressedSize;

		public void Deserialize(BinaryReader Reader)
		{
			CompressedSize = Reader.ReadInt32();
			UncompressedSize = Reader.ReadInt32();
		}
	}

	// Pointed to by FCompressedChunkInfo
	public class FCompressedChunkHeader
	{
		public int Tag;
		public int BlockSize;
		public FCompressedChunkBlock Sum = new FCompressedChunkBlock(); // Total of all blocks

		public void Deserialize(BinaryReader Reader)
		{
			Tag = Reader.ReadInt32();
			BlockSize = Reader.ReadInt32();
			Sum.Deserialize(Reader);
		}
	}

	// From UE4 source
	[Flags]
	public enum ECompressionFlags : int
	{
		/** No compression																*/
		COMPRESS_None = 0x00,
		/** Compress with ZLIB															*/
		COMPRESS_ZLIB = 0x01,
		/** Compress with GZIP															*/
		COMPRESS_GZIP = 0x02,
		/** Prefer compression that compresses smaller (ONLY VALID FOR COMPRESSION)		*/
		COMPRESS_BiasMemory = 0x10,
		/** Prefer compression that compresses faster (ONLY VALID FOR COMPRESSION)		*/
		COMPRESS_BiasSpeed = 0x20,
	}

	// .upk file header
	public class FPackageFileSummary : IUESerializable
	{
		private const uint PACKAGE_FILE_TAG = 0x9E2A83C1;

		public uint Tag;

		public ushort FileVersion, LicenseeVersion;

		public int TotalHeaderSize;
		public FString FolderName = new FString();
		public uint PackageFlags;

		public int NameCount, NameOffset;
		public int ExportCount, ExportOffset;
		public int ImportCount, ImportOffset;
		public int DependsOffset;

		private int Unknown1; // Equal to DependsOffset
		private int Unknown2, Unknown3, Unknown4;

		public FGuid Guid = new FGuid();

		public TArray<FGenerationInfo> Generations = new TArray<FGenerationInfo>();

		public uint EngineVersion, CookerVersion;

		public ECompressionFlags CompressionFlags;

		public TArray<FCompressedChunkInfo> CompressedChunks;

		// Probably a hash
		private int Unknown5;

		private TArray<FString> UnknownStringArray = new TArray<FString>();
		private TArray<FUnknownTypeInSummary> UnknownTypeArray = new TArray<FUnknownTypeInSummary>();

		// Number of bytes of (pos % 0xFF) at the end of the decrypted data, I don't know why it's needed
		public int GarbageSize;

		// Offset to TArray<FCompressedChunkInfo> in decrypted data
		public int CompressedChunkInfoOffset;

		// Size of the last AES block in the encrypted data
		public int LastBlockSize;

		public void Deserialize(BinaryReader Reader)
		{
			Tag = Reader.ReadUInt32();
			if (Tag != PACKAGE_FILE_TAG)
			{
				throw new Exception("Not a valid Unreal Engine package.");
			}

			FileVersion = Reader.ReadUInt16();
			LicenseeVersion = Reader.ReadUInt16();

			TotalHeaderSize = Reader.ReadInt32();
			FolderName.Deserialize(Reader);
			PackageFlags = Reader.ReadUInt32();

			NameCount = Reader.ReadInt32();
			NameOffset = Reader.ReadInt32();

			ExportCount = Reader.ReadInt32();
			ExportOffset = Reader.ReadInt32();

			ImportCount = Reader.ReadInt32();
			ImportOffset = Reader.ReadInt32();

			DependsOffset = Reader.ReadInt32();

			Unknown1 = Reader.ReadInt32();
			Unknown2 = Reader.ReadInt32();
			Unknown3 = Reader.ReadInt32();
			Unknown4 = Reader.ReadInt32();

			Guid.Deserialize(Reader);

			Generations.Deserialize(Reader);

			EngineVersion = Reader.ReadUInt32();
			CookerVersion = Reader.ReadUInt32();

			CompressionFlags = (ECompressionFlags)(Reader.ReadUInt32());

			CompressedChunks = new TArray<FCompressedChunkInfo>(() => new FCompressedChunkInfo(this));
			CompressedChunks.Deserialize(Reader);

			Unknown5 = Reader.ReadInt32();

			UnknownStringArray.Deserialize(Reader);
			UnknownTypeArray.Deserialize(Reader);

			GarbageSize = Reader.ReadInt32();
			CompressedChunkInfoOffset = Reader.ReadInt32();
			LastBlockSize = Reader.ReadInt32();
		}
	}

	// Description of an object that the package exposes
	public class FObjectExport : IUESerializable
	{
		public int ClassIndex, SuperIndex, PackageIndex;
		public long ObjectName; // FName
		public int Archetype;

		public ulong ObjectFlags;

		public int SerialSize;
		public long SerialOffset; // 64 bit if LicenseeVersion >= 22

		public int ExportFlags;
		public TArray<int> NetObjects = new TArray<int>();
		public FGuid PackageGuid = new FGuid();
		public int PackageFlags;

		// To check versions
		private FPackageFileSummary Sum;

		public FObjectExport(FPackageFileSummary InSum)
		{
			Sum = InSum;
		}

		public void Deserialize(BinaryReader Reader)
		{
			ClassIndex = Reader.ReadInt32();
			SuperIndex = Reader.ReadInt32();
			PackageIndex = Reader.ReadInt32();
			ObjectName = Reader.ReadInt64();
			Archetype = Reader.ReadInt32();

			ObjectFlags = Reader.ReadUInt64();

			SerialSize = Reader.ReadInt32();

			if (Sum.LicenseeVersion >= 22)
				SerialOffset = Reader.ReadInt64();
			else
				SerialOffset = Reader.ReadInt32();

			ExportFlags = Reader.ReadInt32();
			NetObjects.Deserialize(Reader);
			PackageGuid.Deserialize(Reader);
			PackageFlags = Reader.ReadInt32();
		}
	}

	class Program
	{
		public static byte[] AESKey =
		{
			0xC7, 0xDF, 0x6B, 0x13, 0x25, 0x2A, 0xCC, 0x71,
			0x47, 0xBB, 0x51, 0xC9, 0x8A, 0xD7, 0xE3, 0x4B,
			0x7F, 0xE5, 0x00, 0xB7, 0x7F, 0xA5, 0xFA, 0xB2,
			0x93, 0xE2, 0xF2, 0x4E, 0x6B, 0x17, 0xE7, 0x79
		};

		// AES decrypt with Rocket League's key
		private static byte[] Decrypt(byte[] Buffer)
		{
			var Rijndael = new RijndaelManaged
			{
				KeySize = 256,
				Key = AESKey,
				Mode = CipherMode.ECB,
				Padding = PaddingMode.None
			};

			var Decryptor = Rijndael.CreateDecryptor();
			return Decryptor.TransformFinalBlock(Buffer, 0, Buffer.Length);
		}

		private static void ProcessFile(string Path, string OutPath)
		{
			using (var Input = File.OpenRead(Path))
			{
				using (var Reader = new BinaryReader(Input))
				{
					var Sum = new FPackageFileSummary();
					Sum.Deserialize(Reader);

					if ((Sum.CompressionFlags & ECompressionFlags.COMPRESS_ZLIB) == 0)
					{
						throw new Exception("Unsupported CompressionFlags");
					}

					// Decrypt the rest of the package header
					var EncryptedSize = Sum.TotalHeaderSize - Sum.GarbageSize - Sum.NameOffset;
					EncryptedSize = (EncryptedSize + 15) & ~15; // Round up to the next block

					var EncryptedData = new byte[EncryptedSize];

					Input.Seek(Sum.NameOffset, SeekOrigin.Begin);
					Input.Read(EncryptedData, 0, EncryptedData.Length);

					var DecryptedData = Decrypt(EncryptedData);

					var ChunkInfo = new TArray<FCompressedChunkInfo>(() => new FCompressedChunkInfo(Sum));

					using (var DecryptedStream = new MemoryStream(DecryptedData))
					{
						using (var DecryptedReader = new BinaryReader(DecryptedStream))
						{
							// Get the compressed chunk info from inside the encrypted data
							DecryptedStream.Seek(Sum.CompressedChunkInfoOffset, SeekOrigin.Begin);
							ChunkInfo.Deserialize(DecryptedReader);

							// Store exports for reserialization
							DecryptedStream.Seek(Sum.ExportOffset - Sum.NameOffset, SeekOrigin.Begin);
						}
					}

					// Copy the original file data
					var FileBuf = new byte[Input.Length];
					Input.Seek(0, SeekOrigin.Begin);
					Input.Read(FileBuf, 0, FileBuf.Length);

					// Save to output file
					using (var Output = File.Open(OutPath, FileMode.Create))
					{
						Output.Write(FileBuf, 0, FileBuf.Length);

						// Write decrypted data
						Output.Seek(Sum.NameOffset, SeekOrigin.Begin);
						Output.Write(DecryptedData, 0, DecryptedData.Length);

						// Decompress compressed chunks
						foreach (var Chunk in ChunkInfo)
						{
							Input.Seek(Chunk.CompressedOffset, SeekOrigin.Begin);
							var Header = new FCompressedChunkHeader();
							Header.Deserialize(Reader);

							var TotalBlockSize = 0;
							var Blocks = new List<FCompressedChunkBlock>();

							while (TotalBlockSize < Header.Sum.UncompressedSize)
							{
								var Block = new FCompressedChunkBlock();
								Block.Deserialize(Reader);
								Blocks.Add(Block);
								TotalBlockSize += Block.UncompressedSize;
							}

							Output.Seek(Chunk.UncompressedOffset, SeekOrigin.Begin);

							foreach (var Block in Blocks)
							{
								var CompressedData = new byte[Block.CompressedSize];
								Input.Read(CompressedData, 0, CompressedData.Length);

								// Zlib inflate
								var ZlibStream = new InflaterInputStream(new MemoryStream(CompressedData));
								ZlibStream.CopyTo(Output);
							}
						}
					}
				}
			}
		}

		static void Main(string[] args)
		{
			if (args.Count() < 1)
			{
				Console.WriteLine("Usage: RLUPKTool <package path>");
				return;
			}

			foreach (var Path in args)
			{
				if (!args[0].EndsWith(".upk"))
				{
					Console.Error.WriteLine($"\"{args[0]}\" should have a .upk extension.");
					continue;
				}
				if (args[0].EndsWith("_decrypted.upk"))
				{
					Console.Error.WriteLine("File is already decrypted.");
					continue;
				}

				ProcessFile(Path, Path.Replace(".upk", "_decrypted.upk"));
			}
		}
	}
}
