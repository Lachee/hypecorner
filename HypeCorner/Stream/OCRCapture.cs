using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.OCR;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.CV.XFeatures2D;
using HypeCorner.Utility;
using HypezoneTwitch.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HypeCorner.Stream
{
    class OCRCapture : IDisposable
    {
        #region Meta Properties
        /// <summary>
        /// Name of the channel
        /// </summary>
        public string ChannelName { get; }

        /// <summary>
        /// Logger
        /// </summary>
        public ILogger Logger { get; set; } = new NullLogger();

        const string LOG_OCR = "OCR ";

        private bool useWindows = false;
        #endregion

        #region OCR Properties

        /// <summary>
        /// Location of the Number Models
        /// </summary>
        public string FeatureModelLocation { get; set; } = @"Resources/feature_{0}.png";

        /// <summary>
        /// Location of the Number List
        /// </summary>
        public string FeatureGridLocation { get; set; } = @"Resources/feature.png";

        /// <summary>Current Frame</summary>
        private Mat _frame = new Mat();
        /// <summary>Cache of image features</summary>
        private FeatureSet _fsGrid;
        private KAZE _kaze;
        #endregion

        #region Restream Properties

        /// <summary>
        /// The current location of the sdp
        /// </summary>
        public string SdpLocation { get; set; } = @"Resources/stream.sdp";

        /// <summary>
        /// The buffer in kilobytes for the FFMPEG stream.
        /// Changes are only applied during the <see cref="Begin"/> as they are passed to the FFMPEG instance.
        /// </summary>
        public int Buffer { get; private set; } = 6000;

        /// <summary>
        /// The current stream.
        /// </summary>
        public Stream Stream => _stream;
        private Stream _stream;

        /// <summary>The current restreamer</summary>
        private Process _restream;

        /// <summary>
        /// The current frame
        /// </summary>
        public int FrameCount => _frameCount;
        private volatile int _frameCount = 0;
        #endregion

        #region Thread Properties
        /// <summary>Are we currently processing?</summary>
        public bool IsRunning => _processing;
        private volatile bool _processing;

        private object _lock;
        private Thread _thread;
        #endregion

        #region Score Properties

        private CyclicList<int> _leftScores, _rightScores;

        /// <summary> Calculated Wins </summary>
        private int l_leftScoreResult, l_rightScoreResult = 0;
        #endregion

        public OCRCapture(string channelName)
        {
            const int cyclicSize = 120;

            this.ChannelName = channelName;

            this._leftScores = new CyclicList<int>(cyclicSize);
            this._rightScores = new CyclicList<int>(cyclicSize);
            this._lock = new object();

            //Prepare detector
            _kaze = new KAZE();

            //Create the grid FS. This one is actually used, the _fsNumbers is depricated.
            using (var img = new Image<Bgr, byte>(FeatureGridLocation))
                _fsGrid = FeatureSet.Detect(_kaze, img.Convert<Gray, byte>());

        }

        #region Getters
        /// <summary>
        /// Gets the current scores
        /// </summary>
        /// <returns></returns>
        public int[] GetScores()
        {
            lock(_lock)
            {
                return new int[]
                {
                    l_leftScoreResult,
                    l_rightScoreResult
                };
            }
        }

        /// <summary>
        /// Determines if the scoreboard is visible
        /// </summary>
        /// <returns></returns>
        public bool IsScoreboardVisible()
        {
            lock (_lock)
            {
                return l_leftScoreResult >= 0 && l_rightScoreResult >= 0;
            }
        }

        /// <summary>
        /// Determines if the player is on match point or not.
        /// </summary>
        /// <returns></returns>
        public bool IsMatchPoint()
        {
            lock(_lock)
            {
                return l_leftScoreResult >= 2 || l_rightScoreResult >= 2;
            }
        }

        #endregion

        #region Threads
        /// <summary>
        /// Starts processing
        /// </summary>
        /// <param name="buffer">Optional size (in kilobytes) of the ffmpeg buffer</param>
        public void Begin(int? buffer = null)
        {
            if (this._processing) throw new InvalidOperationException("Already processing");

            //Apply the buffer
            if (buffer.HasValue)
                Buffer = buffer.Value;

            this._leftScores.Clear();
            this._rightScores.Clear();

            this.l_leftScoreResult = 0;
            this.l_rightScoreResult = 0;
            this._thread = new Thread(Run);
            this._thread.Start();
            this._processing = true;
        }

        public void End()
        {
            //We already aborted, skip
            if (!_processing) return;

            //Abort
            _processing = false;
            _thread.Join();
        }

        private void Run()
        {
            //We are processing
            _processing = true;

            IntPtr ptr = new IntPtr();
            Emgu.CV.CvInvoke.RedirectError((int status, IntPtr funcName, IntPtr errMsg, IntPtr fileName, int line, IntPtr userData) => {
                return 1;
            }, ptr, ptr);
            CvInvoke.SetErrMode(2);

            //Fetch the URL
            try
            {
                var availableStreams = Sniffer.GetStreamsAsync(ChannelName).Result;

                //Determine the stream we will use 
                _stream = availableStreams.Where(s => s.QualityNo == 480).FirstOrDefault();          
            }
            catch(Exception e)
            {
                //Failed to find a stream, abort
                Logger.Error("Failed to sniff streams: {0}", LOG_OCR, e.Message);
                Cleanup();
                return;
            }

            //We didn't find a stream, abort
            if (_stream == null)
            {
                Logger.Error("Failed to find a valid stream!", LOG_OCR);
                Cleanup();
                return;
            }

            //Start the restream
            Logger.Trace("Starting Stream for {0}", LOG_OCR, _stream.Resolution);
            StartRestream(_stream.Url);

            Logger.Trace("Waiting for stream to be ready...", LOG_OCR);
            Thread.Sleep(3000);

            //Start the video capture
            Logger.Info("Capture has started", LOG_OCR);
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "protocol_whitelist;file,rtp,udp");
            using (VideoCapture capture = new VideoCapture(SdpLocation, VideoCapture.API.Ffmpeg))
            {
                //var frame = new Mat();
                while (_processing)
                {
                    try
                    {
                        //Abort
                        if (!capture.IsOpened)
                            break;

                        if (!capture.Grab())
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        //Read the frame and then process it
                        // We may need to exit early.
                        capture.Retrieve(_frame);
                        if (!_processing) break;

                        _frameCount++;
                        ProcessFrame();

                        //Just wait some arbitary time
                        if (useWindows)
                        {
                            if (CvInvoke.WaitKey(33) != -1)
                                break;
                        } 
                        else
                        {
                            CvInvoke.WaitKey(1);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("An exception has occured! {0}", LOG_OCR, e.Message);
                        break;
                    }
                }
            }

            //We want to clean everythign up
            Cleanup();
        }
        
        #endregion

        #region Window
        public void ShowWindows()
        {
            useWindows = true;
        }

        public void HideWindows()
        {
            useWindows = false;
            CvInvoke.DestroyAllWindows();
        }
        #endregion

        #region OCR

        
        /// <summary>
        /// Processes the frame and looks for the stuff
        /// </summary>
        private void ProcessFrame()
        {
            bool useWindows = this.useWindows;

            //Prepare the images. We will use these for the OCR and for drawing debug
            Image<Gray, byte> imgGrayscale = null;
            Image<Bgr, byte> imgColor = null;

            // Convert the images. if we are using windows then we want to incldue the colour one too
            imgGrayscale = _frame.ToImage<Gray, byte>();
            if (useWindows) imgColor = _frame.ToImage<Bgr, byte>();

            //These are positions to crop too
            const int pad = 10;
            const double ratioXLeft = (373-pad) / 852.0;
            const double ratioXRight = (453-pad) / 852.0;
            const double ratioY = (29) / 480.0;
            const double ratioW = (27+pad*2) / 852.0;
            const double ratioH = (24) / 480.0;

            int width = imgGrayscale.Width;
            int height = imgGrayscale.Height;

            //Prepare the rectangles
            Rectangle rectLeft = new Rectangle((int)(ratioXLeft * width), (int)(ratioY * height), (int)(ratioW * width), (int)(ratioH * height));
            Rectangle rectRight = new Rectangle((int)(ratioXRight * width), (int)(ratioY * height), (int)(ratioW * width), (int)(ratioH * height));
            if (useWindows)
            {
                CvInvoke.Rectangle(imgColor, rectLeft, new MCvScalar(255, 0, 255), 1);
                CvInvoke.Rectangle(imgColor, rectRight, new MCvScalar(255, 255, 0), 1);
            }

            //Feature detect the images. These are disposable BTW
            using FeatureSet fsLeft = CreateFeatureSet(imgGrayscale, _kaze, rectLeft);
            using FeatureSet fsRight = CreateFeatureSet(imgGrayscale, _kaze, rectRight);

            //Preform matches on both
            int leftNumber = ComputeFeatureDetection(fsLeft, "Left Feature Set");
            int rightNumber = ComputeFeatureDetection(fsRight, "Right Feature Set");

            _leftScores.Add(leftNumber);
            _rightScores.Add(rightNumber);

          
            //Filter the left scores
            const int filterSize = 3;
            int[] leftFilter = FilterScores(_leftScores, filterSize);
            int[] rightFilter = FilterScores(_rightScores, filterSize);

            int leftScore = (int)Math.Round(leftFilter.Sum() / (double)leftFilter.Length);
            int rightScore = (int)Math.Round(rightFilter.Sum() / (double)rightFilter.Length);

            lock (_lock)
            {
                l_leftScoreResult = leftScore;
                l_rightScoreResult = rightScore;
            }

            //Done, so lets draw the image
            if (useWindows)
            {
                MCvScalar textColor = new MCvScalar(0, 160, 255);
                CvInvoke.PutText(imgColor, $"[ {leftScore} ] <> [ {rightScore} ]", new Point(10, 410), Emgu.CV.CvEnum.FontFace.HersheyPlain, 1, textColor);
                CvInvoke.PutText(imgColor, $"[ {leftNumber} ] || [ {rightNumber} ]", new Point(10, 430), Emgu.CV.CvEnum.FontFace.HersheyPlain, 1, textColor);

                int horizontalScale = (int)(imgColor.Width / (float)_leftScores.Size);
                int verticalScale = 20;

                //Draw the scales
                for (int i = 0; i < 10; i++)
                {
                    var p1 = new Point(0, (verticalScale * (i + 1)) + (verticalScale * 10));
                    var p2 = new Point(imgColor.Width, p1.Y);

                    CvInvoke.Line(imgColor, p1, p2, new MCvScalar(64, 64, 64));
                    CvInvoke.PutText(imgColor, (9 - i) + ":", new Point(10, p1.Y), Emgu.CV.CvEnum.FontFace.HersheyPlain, 1, textColor);
                }


                //Draw the filtered and the sums
                DrawScoreHistogram(imgColor, leftFilter, new MCvScalar(0, 0, 255), 3, horizontalScale, verticalScale);
                //DrawScoreHistogram(imgColor, rightFilter, new MCvScalar(255, 0, 0), 3, horizontalScale, verticalScale);

                //Draw the histograms
                DrawScoreHistogram(imgColor, _leftScores, new MCvScalar(255, 255, 0), 2, horizontalScale, verticalScale);
                DrawScoreHistogram(imgColor, _rightScores, new MCvScalar(0, 255, 255), 2, horizontalScale, verticalScale);

                int avg = leftScore;
                var ap1 = new Point(0, ((10 - avg) * verticalScale) + (verticalScale * 10));
                var ap2 = new Point(imgColor.Width, ap1.Y);
                CvInvoke.Line(imgColor, ap1, ap2, new MCvScalar(255, 0, 0), 1);
                CvInvoke.Imshow("Main Stream - " + ChannelName, imgColor);
            }
        }

        private int[] FilterScores(IEnumerable<int> scores, int filterSize)
        {
            int[] filtered = scores.ToArray();
            for (int i = filterSize; i < filtered.Length - filterSize; i++)
            {
                int sum = 0;
                for (int j = 0; j < filterSize; j++)
                {
                    sum += scores.ElementAt(i - j);
                    sum += scores.ElementAt(i + j);
                }

                filtered[i] = sum / (filterSize + filterSize);
            }

            return filtered;
        }

        /// <summary>
        /// Draws a histogram of scores
        /// </summary>
        private void DrawScoreHistogram(Image<Bgr, byte> output, IEnumerable<int> scores, MCvScalar color, int thickness = 1, int horScale = 1, int verScale = 1)
        {
            const int MAX_SCORE = 10;

            //Draw the left scores
            int i = 0;
            int prev = 0;
            foreach(int curr in scores)
            {
                if (i > 0) 
                {
                    var yOffset = verScale * MAX_SCORE;
                    var prevPoint = new Point((i - 1) * horScale, ((MAX_SCORE - prev) * verScale) + yOffset);
                    var currPoint = new Point(i * horScale, ((MAX_SCORE - curr) * verScale) + yOffset);
                    CvInvoke.Line(output, prevPoint, currPoint, color, thickness);
                }

                prev = curr;
                i += 1;
            }
        }

        /// <summary>
        /// Gets the number that is currently displayed in the observed feature set using feature set matching
        /// </summary>
        /// <param name="fsObserved">The FeatureSet from the current frame.</param>
        /// <param name="observedWindowName">Optional name for the window when <see cref="useWindows"/> is true</param>
        /// <returns></returns>
        private int ComputeFeatureDetection(FeatureSet fsObserved, string observedWindowName = "Observed Image")
        {
            //Peform the match            
            var matches = new VectorOfVectorOfDMatch();
            FindFeatureSetMatches(_fsGrid, fsObserved, matches, out var mask, out var homography);

            //We have no match, abort!
            //if (homography == null) return -1;

            //Prepare the image we will draw
            Image<Bgr, Byte> modelPreview = null, observedPreview = null;
            if (useWindows)
            {
                modelPreview = _fsGrid.image.Convert<Bgr, Byte>();
                observedPreview = fsObserved.image.Convert<Bgr, Byte>();
            }

            //Create a list of tallies
            int[] tallies = new int[10];

            //Find all the matches
            for (int i = 0; i < matches.Size; i++)
            {
                for (int j = 0; j < matches[i].Size; j++)
                {
                    int i1 = matches[i][j].QueryIdx;
                    int i2 = matches[i][j].TrainIdx;

                    var kpOS = fsObserved.keyPoints[i1];
                    var kpFS = _fsGrid.keyPoints[i2];

                    if (mask.IsEmpty || (byte)mask.GetData().GetValue(i, 0) != 0)
                    {
                        //Increment the tally
                        var number = (int)Math.Floor(kpFS.Point.X / 128);
                        tallies[number]++;

                        //Draw the debug if we can
                        if (useWindows)
                        {
                            CvInvoke.Circle(observedPreview, new Point((int)kpOS.Point.X, (int)kpOS.Point.Y), 5, new MCvScalar(0, 0, 255));
                            CvInvoke.Circle(modelPreview, new Point((int)kpFS.Point.X, (int)kpFS.Point.Y), 5, new MCvScalar(0, 0, 255));
                        }
                    }
                    else
                    {
                        //Draw the debug if we can
                        if (useWindows)
                        {
                            CvInvoke.Circle(observedPreview, new Point((int)kpOS.Point.X, (int)kpOS.Point.Y), 5, new MCvScalar(0, 0, 0));
                            CvInvoke.Circle(modelPreview, new Point((int)kpFS.Point.X, (int)kpFS.Point.Y), 5, new MCvScalar(0, 0, 0));
                        }
                    }
                }
            }

            //Find the highest number
            int highestNumber = -1, highestTally = 0;
            for (int i = 0; i < tallies.Length; i++)
            {
                if (tallies[i] > highestTally)
                {
                    highestNumber = i;
                    highestTally = tallies[i];
                }
            }

            //Create two windows for the feature sets
            if (useWindows)
            {
                CvInvoke.Imshow(observedWindowName, observedPreview);
                CvInvoke.Imshow(observedWindowName + " - Model", modelPreview);
            }
            

            //Return the highest number
            return highestNumber;
        }

        /// <summary>
        /// Computes the matches between the two feature set images
        /// </summary>
        /// <param name="model"></param>
        /// <param name="observed"></param>
        /// <param name="matches"></param>
        /// <param name="mask"></param>
        /// <param name="homography"></param>
        private void FindFeatureSetMatches(FeatureSet model, FeatureSet observed, VectorOfVectorOfDMatch matches, out Mat mask, out Mat homography)
        {
            int k = 2;
            double uniquenessThreshold = 0.80;
            homography = null;

            BFMatcher matcher = new BFMatcher(DistanceType.L2);
            matcher.Add(model.descriptors);

            matcher.KnnMatch(observed.descriptors, matches, k, null);
            mask = new Mat(matches.Size, 1, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(255));
            Features2DToolbox.VoteForUniqueness(matches, uniquenessThreshold, mask);

            int nonZeroCount = CvInvoke.CountNonZero(mask);
            if (nonZeroCount >= 4)
            {
                nonZeroCount = Features2DToolbox.VoteForSizeAndOrientation(model.keyPoints, observed.keyPoints, matches, mask, 1.5, 20);
                if (nonZeroCount >= 4)
                    homography = Features2DToolbox.GetHomographyMatrixFromMatchedFeatures(model.keyPoints, observed.keyPoints, matches, mask, 2);
            }
        }

        /// <summary>
        /// Creates a featureset for the current observed frame
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="kaze"></param>
        /// <param name="ROI"></param>
        /// <returns></returns>
        private FeatureSet CreateFeatureSet(Image<Gray, byte> frame, KAZE kaze, Rectangle ROI)
        {
            Image<Gray, byte> observed;
            const int thresholdMin = 70;
            const int thresholdMax = 150;
            const ThresholdType thresholdType = ThresholdType.BinaryInv;

            frame.ROI = ROI;
            observed = frame.Copy();
            observed = observed.Resize(5, Inter.Cubic);
            CvInvoke.Threshold(observed, observed, thresholdMin, thresholdMax, thresholdType);

            return FeatureSet.Detect(kaze, observed);
        }

        #endregion

        #region Restream
        /// <summary>
        /// Starts the restreamer
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private Process StartRestream(string url, bool sync = false, bool verbose = false)
        {
            string rearg = sync ? "-re " : "";

            //Start the process
            Logger.Info("Started FFMPEG process", LOG_OCR);
            _restream =  Process.Start(new ProcessStartInfo()
            {
                FileName = "ffmpeg",
                Arguments = $"-protocol_whitelist file,udp,rtp,http,https,tls,tcp,udp {rearg} -i {url} -vcodec copy -x264-params log-level=panic -an -f rtp rtp://127.0.0.1:1234 -an -bufsize {Buffer}k", //-acodec copy -f rtp rtp://127.0.0.1::1235",
                RedirectStandardOutput = !verbose,
                RedirectStandardError = !verbose,
                UseShellExecute = verbose,
                CreateNoWindow = !verbose,
            });

            _restream.EnableRaisingEvents = true;
            _restream.BeginErrorReadLine();
            _restream.BeginOutputReadLine();
            _restream.OutputDataReceived += (s, e) =>
            {
            };

            _restream.ErrorDataReceived += (s, e) =>
            {
            };

            //When the restream closes, end ourselves.
            _restream.Exited += (s,e) => {
                Cleanup();
            };

            //Return the streamer
            return _restream;
        }
        #endregion

        #region Cleanup

        private void Cleanup()
        {
            //Terminate the restream
            if (_restream != null)
            {
                _restream.EnableRaisingEvents = false;
                _restream?.Kill();
                _restream?.WaitForExit();
                _restream?.Dispose();
                _restream = null;
            }

            //Terminate the thread
            _processing = false;
            _thread = null;

            //Close any windows just in case
            if (useWindows)
                CvInvoke.DestroyAllWindows();

        }

        public void Dispose()
        {
            Cleanup();

            //Remove KAZE
            if (_kaze != null)
            {
                _kaze.Dispose();
                _kaze = null;
            }

            //Ensure the restream HAS ended
            if (_restream != null)
            {
                _restream.Kill();
                _restream.Dispose();
                _restream = null;
            }

            //Dispose the featureset
            if (_fsGrid != null)
            {
                _fsGrid.Dispose();
                _fsGrid = null;
            }

            //Close the thread
            if (_thread != null)
            {
                _processing = false;
                _thread = null;
            }
        }
        #endregion
    }
}


