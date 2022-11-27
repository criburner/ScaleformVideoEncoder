using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using CriMvEncoderControl;

namespace CriMvSimpleEncoder
{
	public class CriMovieEncoder
	{
		private const string CLI_ENCODER_HEARTBEAT_TEXT = ".";

		private const int CLI_ENCODER_HEARTBEAT_TIMEOUT = 12;

		private const int HEARTBEAT_TIMER_INTERVAL = 500;

		private readonly string coreEncoderPath;

		private readonly string coreVideoPlayer;

		private readonly string coreVideoPlayerSwf;

		private readonly string applicationName;

		private Process consoleProcess;

		private DateTime heartbeatTime;

		private Timer heartbeatTimer;

		private bool encoderLaunched;

		private bool encoderCanceled;

		public event EventHandler EncodingExited;

		public event DataReceivedEventHandler EncodingDataReceived;

		public event DataReceivedEventHandler EncodingErrorReceived;

		public event ProgressChangedEventHandler EncodingProgressChenged;

		public CriMovieEncoder(string consoleEncoder, string videoPlayer, string videoPlayerSwf, string appName)
		{
			string location = Assembly.GetEntryAssembly().Location;
			string directoryName = Path.GetDirectoryName(location);
			coreEncoderPath = Path.Combine(directoryName, consoleEncoder);
			coreVideoPlayer = Path.Combine(directoryName, videoPlayer);
			coreVideoPlayerSwf = Path.Combine(directoryName, videoPlayerSwf);
			applicationName = appName;
			initHeartbeatTimer();
			encoderLaunched = false;
			encoderCanceled = false;
		}

		public void ShowErrorMessage(string msg, bool isError)
		{
			string caption = applicationName + " : Error";
			MessageBoxIcon icon = (isError ? MessageBoxIcon.Hand : MessageBoxIcon.Exclamation);
			MessageBox.Show(msg, caption, MessageBoxButtons.OK, icon);
		}

		public bool ShowYesNoMessage(string msg)
		{
			string caption = applicationName + " : Question";
			DialogResult dialogResult = MessageBox.Show(msg, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			return dialogResult == DialogResult.Yes;
		}

		private void updateHeartbeat()
		{
			heartbeatTime = DateTime.Now;
		}

		private void resetHeartbeat()
		{
			heartbeatTime = DateTime.Now;
		}

		private bool checkHeartbeat()
		{
			return (DateTime.Now - heartbeatTime).TotalSeconds < 12.0;
		}

		private void heartbeatTimerProc(object o, EventArgs e)
		{
			if (!checkHeartbeat())
			{
				stopHeartbeatTimer();
				if (ShowYesNoMessage("The encoder does not seem to be responding.  Cancel encoding?"))
				{
					CancelEncode();
				}
				else
				{
					startHeartbeatTimer();
				}
			}
		}

		private void startHeartbeatTimer()
		{
			resetHeartbeat();
			heartbeatTimer.Start();
		}

		private void stopHeartbeatTimer()
		{
			heartbeatTimer.Stop();
		}

		private void initHeartbeatTimer()
		{
			heartbeatTimer = new Timer();
			heartbeatTimer.Interval = 500;
			heartbeatTimer.Tick += heartbeatTimerProc;
		}

		public void StartEncode(EncodingParameters encParam)
		{
			string text = makeArgument(encParam);
			ProcessStartInfo processStartInfo = new ProcessStartInfo(coreEncoderPath, text);
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.RedirectStandardInput = false;
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			consoleProcess = new Process();
			consoleProcess.StartInfo = processStartInfo;
			consoleProcess.EnableRaisingEvents = true;
			consoleProcess.OutputDataReceived += criMovieEncoder_OutputDataReceived;
			consoleProcess.ErrorDataReceived += criMovieEncoder_ErrorDataReceived;
			consoleProcess.Exited += criMovieEncoder_Exited;
			try
			{
				consoleProcess.Start();
				consoleProcess.BeginOutputReadLine();
				consoleProcess.BeginErrorReadLine();
				encoderLaunched = true;
				encoderCanceled = false;
				startHeartbeatTimer();
				generateEncodeBatchFile(text, encParam);
			}
			catch (Exception)
			{
				ShowErrorMessage("There was an error starting the encoder.\nPlease make sure that\n\n\t'" + coreEncoderPath + "'\n\nexists and is runnable.", true);
			}
		}

		public void CancelEncode()
		{
			if (encoderCanceled)
			{
				return;
			}
			encoderCanceled = true;
			stopHeartbeatTimer();
			try
			{
				if (!consoleProcess.HasExited)
				{
					consoleProcess.Kill();
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				if (this.EncodingProgressChenged != null)
				{
					this.EncodingProgressChenged(this, new ProgressChangedEventArgs(100, null));
				}
			}
		}

		private string makeArgument(EncodingParameters encParam)
		{
			StringBuilder stringBuilder = new StringBuilder("-gui_mode -preview=off");
			stringBuilder.AppendFormat(" -heartbeat");
			stringBuilder.AppendFormat(" -gop_closed=on -gop_i=1 -gop_p=4 -gop_b=2");
			stringBuilder.AppendFormat(" -video00=\"{0}\"", encParam.inputVideoFilePath);
			stringBuilder.AppendFormat(" -output=\"{0}\"", encParam.outputFilePath);
			stringBuilder.AppendFormat(" -bitrate={0}", encParam.bitrate * 1000);
			if (!encParam.useInputFramerate && encParam.framerateBase != 0f && encParam.framerateScale != 0f)
			{
				stringBuilder.AppendFormat(" -framerate={0},{1}", encParam.framerateBase, encParam.framerateScale);
			}
			if (encParam.useHCA)
			{
				stringBuilder.AppendFormat(" -hca=on");
				stringBuilder.AppendFormat(" -hca_quality={0}", encParam.hcaQuality);
			}
			if (encParam.enableResize)
			{
				stringBuilder.AppendFormat(" -scale={0},{1}", encParam.resizeWidth, encParam.resizeHeight);
			}
			if (encParam.isTargetPS2)
			{
				stringBuilder.AppendFormat(" -ps2=on");
			}
			if (encParam.useAlphaChannel)
			{
				stringBuilder.AppendFormat(" -alpha00=\"{0}\"", encParam.inputVideoFilePath);
			}
			for (int i = 0; i < 32; i++)
			{
				AudioParameters audioParameters = encParam.langParams[i];
				switch (audioParameters.AudioType)
				{
				case InputAudioType.MonoOrStereo:
					stringBuilder.AppendFormat(" -audio{0:d2}=\"{1}\"", i, audioParameters.FilePathMonoStereo);
					break;
				case InputAudioType.MultiChannel:
					stringBuilder.AppendFormat(" -mca{0:d2}_00=\"{1}\"", i, audioParameters.FilePath51Left);
					stringBuilder.AppendFormat(" -mca{0:d2}_01=\"{1}\"", i, audioParameters.FilePath51Right);
					stringBuilder.AppendFormat(" -mca{0:d2}_02=\"{1}\"", i, audioParameters.FilePath51LeftSurround);
					stringBuilder.AppendFormat(" -mca{0:d2}_03=\"{1}\"", i, audioParameters.FilePath51RightSurround);
					stringBuilder.AppendFormat(" -mca{0:d2}_04=\"{1}\"", i, audioParameters.FilePath51Center);
					stringBuilder.AppendFormat(" -mca{0:d2}_05=\"{1}\"", i, audioParameters.FilePath51LFE);
					break;
				}
			}
			for (int j = 0; j < 32; j++)
			{
				if (!encParam.subtitleFilePaths[j].Equals(string.Empty))
				{
					stringBuilder.AppendFormat(" -subtitle{0:d2}=\"{1}\"", j, encParam.subtitleFilePaths[j]);
				}
			}
			if (!encParam.cuepointFilePath.Equals(string.Empty))
			{
				stringBuilder.AppendFormat(" -cuepoint=\"{0}\"", encParam.cuepointFilePath);
			}
			return stringBuilder.ToString();
		}

		private void generateEncodeBatchFile(string args, EncodingParameters encParam)
		{
			string text = Path.ChangeExtension(encParam.outputFilePath, ".bat");
			try
			{
				TextWriter textWriter = new StreamWriter(text);
				string text2 = args.Replace("-gui_mode", "");
				text2 = text2.Replace("-preview=off", "");
				text2 = text2.Replace("-heartbeat", "");
				textWriter.WriteLine("\"{0}\" {1}", Path.GetFullPath(coreEncoderPath), text2);
				textWriter.Close();
			}
			catch (Exception)
			{
				ShowErrorMessage("There was an error creating the re-encoding batch file:\n\n" + text + "\n\nPlease check your file permissions.", false);
			}
		}

		public void Preview(string filename, bool extendedPlayer)
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (extendedPlayer)
			{
				if (!File.Exists(coreVideoPlayerSwf))
				{
					ShowErrorMessage("The extended video player\n\n\t'" + coreVideoPlayerSwf + "'\n\nis not available.  Please make sure it is installed into the same directory as the encoder.", true);
					return;
				}
				stringBuilder.AppendFormat("\"{0}\" -arg \"{1}\"", Path.GetFullPath(coreVideoPlayerSwf), filename);
			}
			else
			{
				stringBuilder.AppendFormat("\"{0}\"", filename);
			}
			string arguments = stringBuilder.ToString();
			ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.GetFullPath(coreVideoPlayer), arguments);
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			try
			{
				consoleProcess = new Process();
				consoleProcess.StartInfo = processStartInfo;
				consoleProcess.Start();
			}
			catch (Exception)
			{
				ShowErrorMessage("There was an error starting the previewer.\nPlease make sure that\n\n\t'" + coreVideoPlayer + "'\n\nexists and is runnable.", true);
			}
		}

		public void Load(string filename)
		{
		}

		private static int? IsProgressText(string text)
		{
			int result = 0;
			if (int.TryParse(text, out result))
			{
				return result;
			}
			return null;
		}

		private static bool IsHeartbeatText(string text)
		{
			return text.Trim().Equals(".");
		}

		private void displayReceivedData(DataReceivedEventHandler handler, object sender, DataReceivedEventArgs e)
		{
			if (string.IsNullOrEmpty(e.Data))
			{
				return;
			}
			int? num = IsProgressText(e.Data);
			if (num.HasValue)
			{
				if (this.EncodingProgressChenged != null)
				{
					this.EncodingProgressChenged(sender, new ProgressChangedEventArgs(num.Value, null));
				}
			}
			else if (IsHeartbeatText(e.Data))
			{
				updateHeartbeat();
			}
			else if (handler != null)
			{
				handler(sender, e);
			}
		}

		private void criMovieEncoder_OutputDataReceived(object sender, DataReceivedEventArgs e)
		{
			displayReceivedData(this.EncodingDataReceived, sender, e);
		}

		private void criMovieEncoder_ErrorDataReceived(object sender, DataReceivedEventArgs e)
		{
			displayReceivedData(this.EncodingErrorReceived, sender, e);
		}

		private void criMovieEncoder_Exited(object sender, EventArgs e)
		{
			stopHeartbeatTimer();
			if (this.EncodingProgressChenged != null)
			{
				this.EncodingProgressChenged(this, new ProgressChangedEventArgs(100, null));
			}
			if (this.EncodingExited != null)
			{
				this.EncodingExited(sender, e);
			}
			try
			{
				if (encoderLaunched)
				{
					consoleProcess.Close();
					consoleProcess.Dispose();
					encoderLaunched = false;
				}
			}
			catch
			{
			}
		}
	}
}
