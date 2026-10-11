using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskBox.Infrastructure;
using DeskBox.Interop;

namespace DeskBox.Services;

public static class IconService
{
	private static readonly ConcurrentDictionary<string, ImageSource> Cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

	public static bool Monochrome { get; set; }

	public static Color MonochromeColor { get; set; } = Colors.White;

	private static readonly object LoadLock = new();
	private static double RenderScale = 1;
	public static ImageSource Placeholder { get; } = CreatePlaceholder();
	private static ImageSource CreatePlaceholder()
	{
		var drawing = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(110, 130, 155)), null,
			Geometry.Parse("M4,2 L20,2 28,10 28,30 4,30 Z M20,2 L20,10 28,10"));
		var image = new DrawingImage(drawing);
		image.Freeze();
		return image;
	}

	public static ImageSource? GetForItem(DeskBox.Models.DeskItem item, int size, bool mono, Color color, double dpi)
	{
		lock (LoadLock)
		{
			Monochrome = mono;
			MonochromeColor = color;
			RenderScale = dpi;
			if (item.IsGroup) return GetFolderIcon(size) ?? Placeholder;
			string stored = AppPaths.StoredFilePath(item);
			if (!string.IsNullOrEmpty(stored) && File.Exists(stored)) return GetIcon(stored, size) ?? Placeholder;
			if (!string.IsNullOrEmpty(item.Target)) return GetIcon(item.Target, size) ?? Placeholder;
			return GetIconFallback(item.StoredName ?? ".exe", size) ?? Placeholder;
		}
	}

	private static string CacheKey(string path, int size)
	{
		if (!Monochrome)
		{
			return $"{path}|{size}|{RenderScale}";
		}
		return $"{path}|{size}|{RenderScale}|mono|{MonochromeColor}";
	}

	private static ImageSource LineArt(BitmapSource source, Color color)
	{
		try
		{
			BitmapSource bitmapSource = ((source.Format == PixelFormats.Bgra32) ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0));
			int pixelWidth = bitmapSource.PixelWidth;
			int pixelHeight = bitmapSource.PixelHeight;
			if (pixelWidth < 8 || pixelHeight < 8)
			{
				return source;
			}
			int num = pixelWidth * 4;
			byte[] array = new byte[pixelHeight * num];
			bitmapSource.CopyPixels(array, num, 0);
			float[] array2 = new float[pixelWidth * pixelHeight];
			float[] array3 = new float[pixelWidth * pixelHeight];
			for (int i = 0; i < pixelHeight; i++)
			{
				for (int j = 0; j < pixelWidth; j++)
				{
					int num2 = i * num + j * 4;
					float num3 = (float)(int)array[num2 + 3] / 255f;
					float num4 = (0.299f * (float)(int)array[num2 + 2] + 0.587f * (float)(int)array[num2 + 1] + 0.114f * (float)(int)array[num2]) / 255f;
					array3[i * pixelWidth + j] = num3;
					array2[i * pixelWidth + j] = num4 * num3;
				}
			}
			float[] array4 = new float[pixelWidth * pixelHeight];
			float num5 = 0f;
			for (int k = 1; k < pixelHeight - 1; k++)
			{
				for (int l = 1; l < pixelWidth - 1; l++)
				{
					float num6 = 0f;
					float num7 = 0f;
					float num8 = 0f;
					float num9 = 0f;
					for (int m = -1; m <= 1; m++)
					{
						for (int n = -1; n <= 1; n++)
						{
							int num10 = n * ((m != 0) ? 1 : 2);
							int num11 = m * ((n != 0) ? 1 : 2);
							float num12 = array2[(k + m) * pixelWidth + (l + n)];
							float num13 = array3[(k + m) * pixelWidth + (l + n)];
							num6 += num12 * (float)num10;
							num7 += num12 * (float)num11;
							num8 += num13 * (float)num10;
							num9 += num13 * (float)num11;
						}
					}
					float num14 = (array4[k * pixelWidth + l] = MathF.Sqrt(num6 * num6 + num7 * num7) + 0.5f * MathF.Sqrt(num8 * num8 + num9 * num9));
					if (num14 > num5)
					{
						num5 = num14;
					}
				}
			}
			if (num5 < 0.02f)
			{
				return source;
			}
			byte[] array5 = new byte[pixelHeight * num];
			float num15 = num5 * 0.16f;
			float num16 = MathF.Max(num5 - num15, 0.0001f);
			for (int num17 = 0; num17 < pixelHeight; num17++)
			{
				for (int num18 = 0; num18 < pixelWidth; num18++)
				{
					float num19 = array4[num17 * pixelWidth + num18];
					for (int num20 = -1; num20 <= 1; num20++)
					{
						if (!(num19 < num5))
						{
							break;
						}
						int num21 = num17 + num20;
						if (num21 < 0 || num21 >= pixelHeight)
						{
							continue;
						}
						for (int num22 = -1; num22 <= 1; num22++)
						{
							int num23 = num18 + num22;
							if (num23 >= 0 && num23 < pixelWidth)
							{
								float num24 = array4[num21 * pixelWidth + num23];
								if (num24 > num19)
								{
									num19 = num24;
								}
							}
						}
					}
					if (!(num19 <= num15))
					{
						float num25 = Math.Min(1f, (num19 - num15) / num16 * 1.8f);
						int num26 = num17 * num + num18 * 4;
						array5[num26] = color.B;
						array5[num26 + 1] = color.G;
						array5[num26 + 2] = color.R;
						array5[num26 + 3] = (byte)(num25 * 255f);
					}
				}
			}
			BitmapSource bitmapSource2 = BitmapSource.Create(pixelWidth, pixelHeight, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, array5, num);
			((Freezable)bitmapSource2).Freeze();
			return bitmapSource2;
		}
		catch (Exception ex)
		{
			AppPaths.Log("线条化图标失败：" + ex.Message);
			return source;
		}
	}

	private static ImageSource? ApplyStyle(ImageSource? image)
	{
		if (!(image is BitmapSource source) || !Monochrome)
		{
			return image;
		}
		return LineArt(source, MonochromeColor);
	}

	public static ImageSource? GetIcon(string path, int size)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}
		string key = CacheKey(path, size);
		if (Cache.TryGetValue(key, out ImageSource value))
		{
			return value;
		}
		ImageSource image = LoadFromShell(path, size) ?? LoadFallback(path, size);
		image = ApplyStyle(image);
		if (image != null)
		{
			Cache[key] = image;
		}
		return image;
	}

	public static ImageSource? GetFolderIcon(int size)
	{
		string key = CacheKey("::stock-folder::", size);
		if (Cache.TryGetValue(key, out ImageSource value))
		{
			return value;
		}
		string path = AppPaths.ItemsDir;
		try
		{
			Directory.CreateDirectory(path);
		}
		catch
		{
		}
		if (!Directory.Exists(path))
		{
			path = AppPaths.BaseDir;
		}
		if (!Directory.Exists(path))
		{
			path = AppPaths.ExeDir;
		}
		ImageSource imageSource = ApplyStyle(GetIcon(path, size) ?? LoadStockFolderIcon(size));
		if (imageSource != null)
		{
			Cache[key] = imageSource;
		}
		return imageSource;
	}

	private static ImageSource? LoadStockFolderIcon(int size)
	{
		Native.SHSTOCKICONINFO psii = new Native.SHSTOCKICONINFO
		{
			cbSize = (uint)Marshal.SizeOf<Native.SHSTOCKICONINFO>()
		};
		try
		{
			if (Native.SHGetStockIconInfo(3, 256u, ref psii) != 0 || psii.hIcon == IntPtr.Zero)
			{
				return null;
			}
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(psii.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			((Freezable)bitmapSource).Freeze();
			return Resize(bitmapSource, size);
		}
		catch (Exception ex)
		{
			AppPaths.Log("取系统文件夹图标失败：" + ex.Message);
			return null;
		}
		finally
		{
			if (psii.hIcon != IntPtr.Zero)
			{
				Native.DestroyIcon(psii.hIcon);
			}
		}
	}

	public static ImageSource? GetIconFallback(string path, int size)
	{
		string key = CacheKey(path + "|fallback", size);
		if (Cache.TryGetValue(key, out ImageSource value))
		{
			return value;
		}
		ImageSource imageSource = ApplyStyle(LoadFallback(path, size));
		if (imageSource != null)
		{
			Cache[key] = imageSource;
		}
		return imageSource;
	}

	private static ImageSource? LoadFromShell(string path, int size)
	{
		nint phbm = IntPtr.Zero;
        Native.IShellItemImageFactory? shellItemImageFactory = null;
		try
		{
			if (!File.Exists(path) && !Directory.Exists(path))
			{
				return null;
			}
			Guid riid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
			shellItemImageFactory = Native.SHCreateItemFromParsingName(path, IntPtr.Zero, riid);
			if (shellItemImageFactory == null)
			{
				return null;
			}
			int num = Math.Max(size, 48) * 2;
			shellItemImageFactory.GetImage(new Native.SIZE(num, num), 5, out phbm);
			if (phbm == IntPtr.Zero)
			{
				return null;
			}
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(phbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			((Freezable)bitmapSource).Freeze();
			return Resize(bitmapSource, size);
		}
		catch (Exception ex)
		{
			AppPaths.Log("取图标失败 " + path + "：" + ex.Message);
			return null;
		}
		finally
		{
			if (phbm != IntPtr.Zero)
			{
				Native.DeleteObject(phbm);
			}
            if (shellItemImageFactory != null) Marshal.FinalReleaseComObject(shellItemImageFactory);
		}
	}

	private static ImageSource? LoadFallback(string path, int size)
	{
		nint num = IntPtr.Zero;
		try
		{
			Native.SHFILEINFO psfi = default;
			uint num2 = 256u;
			if (!File.Exists(path) && !Directory.Exists(path))
			{
				num2 |= 0x10;
			}
			Native.SHGetFileInfo(path, 128u, ref psfi, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), num2);
			num = psfi.hIcon;
			if (num == IntPtr.Zero)
			{
				return null;
			}
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(num, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			((Freezable)bitmapSource).Freeze();
			return Resize(bitmapSource, size);
		}
		catch
		{
			return null;
		}
		finally
		{
			if (num != IntPtr.Zero)
			{
				Native.DestroyIcon(num);
			}
		}
	}

	private static ImageSource Resize(BitmapSource source, int size)
	{
		double num = RenderScale;
		int num2 = (int)Math.Round((double)size * num);
		if (num2 <= 0)
		{
			num2 = size;
		}
		if (source.PixelWidth == num2 && source.PixelHeight == num2)
		{
			return source;
		}
		TransformedBitmap transformedBitmap = new TransformedBitmap(source, new ScaleTransform((double)num2 / (double)source.PixelWidth, (double)num2 / (double)source.PixelHeight));
		((Freezable)transformedBitmap).Freeze();
		return transformedBitmap;
	}
}
