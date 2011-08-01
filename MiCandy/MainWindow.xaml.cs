using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Timers;
using System.Threading;
using Microsoft.Research.Kinect;
using Microsoft.Research.Kinect.Audio;
using Microsoft.Win32;

namespace MiCandy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {     

        /// <summary>
        /// the name of the temporary file used to hold the current record
        /// </summary>
        string _temporaryWavfile;

        /// <summary>
        /// the length of the current recording time in fractional seconds
        /// </summary>
        double _recordingTime;

        double _recordTimerIntervalMilliseconds = 10;

        System.Timers.Timer _recordTimer;

        /// <summary>
        /// a flag to indicate whether recording is in progress
        /// value is false before recording starts or after recording is stopped by the user
        /// </summary>
        bool _isRecording = false;



        public MainWindow()
        {
            InitializeComponent();

            _temporaryWavfile = String.Format("{0}\\{1}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "miCandyRecord.wav");         
            _recordTimer = new System.Timers.Timer(_recordTimerIntervalMilliseconds);
            _recordTimer.Elapsed += new ElapsedEventHandler(_recordTimer_Elapsed);
        }

        void _recordTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            //increment time of the record
            _recordingTime += (_recordTimerIntervalMilliseconds / 100);
        }


        private void label1_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //minimize the window
            MiCandyWindow.WindowState = System.Windows.WindowState.Minimized;
        }

        private void label2_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //close app
            this.Close();
        }

        private void btnRecord_Click(object sender, RoutedEventArgs e)
        {
            //only proceed if a kinect sensor is available
            if (!testKinectPresence()) return;

            btnRecord.IsEnabled = false;
            btnPreview.IsEnabled = false;
            btnSave.IsEnabled = false;
            btnStop.IsEnabled = true;

            _isRecording = true;

            var t = new Thread(new ThreadStart(RecordAudio));
            t.SetApartmentState(ApartmentState.MTA);

            t.Start();

        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        { 
            //stop recording
            _isRecording = false;            
        }

        private void stopRecording()
        {
            //stop counting recording time
            _recordTimer.Stop();

            //we can then preview & save
            btnPreview.IsEnabled = true;
            btnSave.IsEnabled = true;
            btnRecord.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void btnPreview_Click(object sender, RoutedEventArgs e)
        {
            btnRecord.IsEnabled = false;
            btnSave.IsEnabled = false;

            playAudio(_temporaryWavfile);           
        }

        private void playAudio(string audioPath)
        {
            try
            {
                AudioElement.Source = new Uri(audioPath, UriKind.RelativeOrAbsolute);
                AudioElement.LoadedBehavior = MediaState.Play;
                AudioElement.UnloadedBehavior = MediaState.Stop;
            }
            catch (Exception)
            {
                btnRecord.IsEnabled = true;
            }
        }

        private void RecordAudio()
        {
           // var recordingTime = 2 * 16000 * _recordingTime;
            var audioDataMemomryStream = new MemoryStream();

            using (var KinectAudio = new KinectAudioSource())
            {
                KinectAudio.SystemMode = SystemMode.OptibeamArrayOnly;

                var count = 0;
                var totalCount = 0;

                var buffer = new byte[1024];

                using (var audioStream = KinectAudio.Start())
                {
                    //start counting the recording time
                    _recordingTime = 0;
                    _recordTimer.Start();

                    while (((count = audioStream.Read(buffer, 0, buffer.Length)) > 0) && _isRecording)
                    {
                        totalCount += count;
                        audioDataMemomryStream.Write(buffer, 0, buffer.Length);
                    }

                    Dispatcher.Invoke((Action)delegate
                    {
                        stopRecording();
                    });
                }


                //write the combined file now                
                var recordingTime = 2 * 16000 * _recordingTime;

                Stream fileStream = File.Open(_temporaryWavfile, FileMode.Create);

                using (fileStream)
                {
                    //start with the header
                    WriteWavHeader(fileStream, (int)recordingTime);
                    //then append the audio data to it
                    fileStream.Write(audioDataMemomryStream.ToArray(), 0, (int)audioDataMemomryStream.Length);
                }

                //please close resources
                audioDataMemomryStream.Close();
                fileStream.Close();
            }
        }

        private void MiCandyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            testKinectPresence();
        }

        private bool testKinectPresence()
        {
            try
            {
                Microsoft.Research.Kinect.Audio.AudioDeviceInfo info = new AudioDeviceInfo();
                tbxStatus.Text = String.Format("a kinect sensor is detected", info.DeviceName);

                return true;
            }
            catch (Exception)
            {
                tbxStatus.Text = "please connect a kinect sensor";
                return false;
            }
        }

        /// <summary>
        /// A bare bones WAV file header writer
        /// </summary>        
        static void WriteWavHeader(Stream stream, int dataLength)
        {
            //We need to use a memory stream because the BinaryWriter will close the underlying stream when it is closed
            using (var memStream = new MemoryStream(64))
            {
                int cbFormat = 18; //sizeof(WAVEFORMATEX)
                WAVEFORMATEX format = new WAVEFORMATEX()
                {
                    wFormatTag = 1,
                    nChannels = 1,
                    nSamplesPerSec = 16000,
                    nAvgBytesPerSec = 32000,
                    nBlockAlign = 2,
                    wBitsPerSample = 16,
                    cbSize = 0
                };

                using (var bw = new BinaryWriter(memStream))
                {
                    //RIFF header
                    WriteString(memStream, "RIFF");
                    bw.Write(dataLength + cbFormat + 4); //File size - 8
                    WriteString(memStream, "WAVE");
                    WriteString(memStream, "fmt ");
                    bw.Write(cbFormat);

                    //WAVEFORMATEX
                    bw.Write(format.wFormatTag);
                    bw.Write(format.nChannels);
                    bw.Write(format.nSamplesPerSec);
                    bw.Write(format.nAvgBytesPerSec);
                    bw.Write(format.nBlockAlign);
                    bw.Write(format.wBitsPerSample);
                    bw.Write(format.cbSize);

                    //data header
                    WriteString(memStream, "data");
                    bw.Write(dataLength);
                    memStream.WriteTo(stream);
                }
            }
        }

        static void WriteString(Stream stream, string s)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(s);
            stream.Write(bytes, 0, bytes.Length);
        }

        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        private void AudioElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            btnPreview.IsEnabled = false;
        }

        private void AudioElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            btnPreview.IsEnabled = true;
            btnRecord.IsEnabled = true;
            btnSave.IsEnabled = true;            
            //release the audio file
            AudioElement.Source = null;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.DefaultExt = "wav";            
            dialog.ShowDialog(this);
            try
            {                
                String outputFile = dialog.FileName;
                File.Copy(_temporaryWavfile, outputFile);
            }
            catch (Exception)
            { }
            
        }       
    }
}
