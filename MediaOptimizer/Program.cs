using ImageMagick;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaOptimizer
{
	class Program
	{
		private static HashSet<string> _processedFiles = new HashSet<string>();
		private static string[] _extentionsToProcess = new[] { ".png", ".svg", ".gif", ".jpg", ".jpeg" };
		private static int _maxWidth = 3840;
		private static int _maxHeight = 2160;
		private static int _quality = 100;
		private static int _threadCount = 1;
		private const string SIGNATURE = "ByMediaOptimizer";

		static void Main(string[] args)
		{
			while (true)
			{
				_processedFiles.Clear();
				WriteToConsole("Enter directory path : ");
				var dirPath = Console.ReadLine();
				bool dirPathValid = Directory.Exists(dirPath);

				while (!dirPathValid)
				{
					WriteLineToConsole($"Directory {dirPath} doesn't exist.");
					WriteToConsole("Enter directory path : ");
					dirPath = Console.ReadLine();
					dirPathValid = Directory.Exists(dirPath);
				}

				string readValue = string.Empty;

				WriteToConsole($"Enter number of threads to run (default {_threadCount}) : ");
				readValue = Console.ReadLine().Trim();
				if (!string.IsNullOrWhiteSpace(readValue))
				{
					int.TryParse(readValue, out _threadCount);
				}
				WriteLineToConsole($"Thread count set to : {_threadCount}");

				WriteToConsole($"Enter extensions to process (default {string.Join(", ", _extentionsToProcess)}) : ");
				readValue = Console.ReadLine().Trim();
				if (!string.IsNullOrWhiteSpace(readValue))
				{
					_extentionsToProcess = readValue
						.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(e => e.Trim()).ToArray();
				}
				WriteLineToConsole($"Extensions set to : {string.Join(", ", _extentionsToProcess)}");

				WriteToConsole($"Enter max width (0 to ignore) (default {_maxWidth}) : ");
				readValue = Console.ReadLine().Trim();
				if (!string.IsNullOrWhiteSpace(readValue))
				{
					int.TryParse(readValue, out _maxWidth);
					_maxWidth = _maxWidth < 0 ? 0 : _maxWidth;
				}
				WriteLineToConsole($"Max width set to (0 to ignore) : {_maxWidth}");

				WriteToConsole($"Enter max height (default {_maxHeight}) : ");
				readValue = Console.ReadLine().Trim();
				if (!string.IsNullOrWhiteSpace(readValue))
				{
					int.TryParse(readValue, out _maxHeight);
					_maxHeight = _maxHeight < 0 ? 0 : _maxHeight;
				}
				WriteLineToConsole($"Max height set to : {_maxHeight}");

				WriteToConsole($"Enter quality between 1-100 (default {_quality}) : ");
				readValue = Console.ReadLine().Trim();
				if (!string.IsNullOrWhiteSpace(readValue))
				{
					int.TryParse(readValue, out _quality);
				}
				WriteLineToConsole($"Quality set to : {_quality}");

				var executingTasks = new List<Task>();
				for (int i = 0; i < _threadCount; i++)
				{
					WriteLineToConsole("Spawning thread NO. " + i);
					var curTask = Task.Run(() =>
					{
						try
						{
							ProcessDirectoryFiles(dirPath);
						}
						catch (Exception ex)
						{
							WriteToConsole($"Error processing directory : {dirPath}, Error : {ex.Message}", ConsoleColor.Red);
						}
					});
					executingTasks.Add(curTask);
				}

				Task.WhenAll(executingTasks).Wait();

				WriteLineToConsole("All directories processed.", ConsoleColor.Yellow);
			}
		}

		private static void ProcessDirectoryFiles(string dirPath)
		{
			WriteLineToConsole("Processing directory : " + dirPath);

			foreach (var subDir in Directory.GetDirectories(dirPath))
			{
				ProcessDirectoryFiles(subDir);
			}

			foreach (var file in Directory.GetFiles(dirPath).Where(p => _extentionsToProcess.Any(p.ToLower().EndsWith)).ToList())
			{
				lock (_processedFiles)
				{
					if (_processedFiles.Contains(file)) continue;

					_processedFiles.Add(file);
				}

				WriteLineToConsole("Processing file : " + file, ConsoleColor.DarkGreen);

				try
				{
					var imgInfo = new FileInfo(file);
					var sizeBefore = imgInfo.Length / 1024d;

					using (var img = new MagickImage(imgInfo))
					{
						// check if image was already processed
						//var curProfile = img.GetExifProfile();
						//var comment = curProfile?.GetValue(ExifTag.UserComment)?.Value?.ToString();
						//if (!string.IsNullOrWhiteSpace(comment) && comment.Contains(SIGNATURE))
						//{
						//	WriteLineToConsole($"Skipping previously processed file {file}");
						//	continue;
						//}

						// remove metadatas
						img.Strip();

						// add processed tag to prevent re-processing in the future
						//var profile = new ExifProfile();
						//profile.SetValue(ExifTag.UserComment, Encoding.UTF8.GetBytes(SIGNATURE));
						//img.AddProfile(profile, true);

						img.Quality = _quality;

						int targetHeight = img.Height <= _maxHeight || _maxHeight <= 0 ? img.Height : _maxHeight;
						int targetWidth = img.Width <= _maxWidth || _maxHeight <= 0 ? img.Width : _maxWidth;

						// process gif
						if (file.ToLower().EndsWith(".gif"))
						{
							using (MagickImageCollection collection = new MagickImageCollection(imgInfo))
							{
								foreach (MagickImage image in collection)
								{
									image.Quality = _quality;
									//image.Resize(targetWidth, targetHeight);
									//image.Quantize(new QuantizeSettings()
									//{
									//	Colors = img.TotalColors, //img.TotalColors > 256 ? 256 : img.TotalColors,
									//	DitherMethod = DitherMethod.No
									//});
								}

								collection.Quantize(new QuantizeSettings()
								{
									Colors = img.TotalColors > 256 ? 256 : img.TotalColors,
									DitherMethod = DitherMethod.No
								});

								//collection.Optimize();
								//collection.OptimizeTransparency();
								//collection.Coalesce();

								// skip writing if size reduction fails
								if (collection.ToByteArray().Length < imgInfo.Length)
								{
									collection.Write(imgInfo);
								}
							}
						}
						else
						{
							if ((targetWidth > 0 || targetHeight > 0) && (targetWidth != img.Width || targetHeight != img.Height))
								img.AdaptiveResize(targetWidth, targetHeight);

							// skip writing if size reduction fails
							if (img.ToByteArray().Length < imgInfo.Length)
							{
								img.Write(imgInfo);
							}
						}
					}

					// compress
					var optimizer = new ImageOptimizer()
					{
						IgnoreUnsupportedFormats = true,
						OptimalCompression = true,
					};
					optimizer.Compress(imgInfo);
					optimizer.LosslessCompress(imgInfo);

					imgInfo.Refresh();

					var sizeAfter = imgInfo.Length / 1024d;
					double reductionPercent = ((sizeBefore - sizeAfter) / (double)sizeBefore) * 100;
					WriteLineToConsole($"File processed : {file} " +
						$"{Environment.NewLine}" +
						$"Before : {sizeBefore.ToString("0.##")}KB, " +
						$"After : {sizeAfter.ToString("0.##")}KB, " +
						$"{reductionPercent.ToString("0.##")}% reduction.",
						reductionPercent < 0 ? ConsoleColor.Red : ConsoleColor.Cyan
					);
				}
				catch (Exception ex)
				{
					WriteLineToConsole($"Error processing file : {file}, Error : {ex.Message}", ConsoleColor.Red);
				}
			}
			WriteLineToConsole("Completed processing directory : " + dirPath, ConsoleColor.Blue);
		}

		private static void WriteLineToConsole(string text, ConsoleColor foregroundColor = ConsoleColor.White, bool inline = false)
		{
			var curForegroundColor = Console.ForegroundColor;

			Console.ForegroundColor = foregroundColor;
			if (inline) Console.Write(text); else Console.WriteLine(text);
			Console.ForegroundColor = curForegroundColor;
		}

		private static void WriteToConsole(string text, ConsoleColor foregroundColor = ConsoleColor.White)
		{
			WriteLineToConsole(text, foregroundColor, true);
		}
	}
}

//dotnet publish -r win10-x64 -p:PublishSingleFile=true
