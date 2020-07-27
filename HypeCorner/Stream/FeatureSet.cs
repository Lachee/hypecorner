using Emgu.CV;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace HypeCorner.Stream
{
    /// <summary>
    /// Contains points of interests for a image.
    /// </summary>
    class FeatureSet : IDisposable
    {
        /// <summary>
        /// Image the feature set is based off
        /// </summary>
        public Image<Gray, byte> image;
        
        /// <summary>
        /// Descriptors of the feature set
        /// </summary>
        public Mat descriptors;

        /// <summary>
        /// Keypoints of the feature set
        /// </summary>
        public VectorOfKeyPoint keyPoints;

        /// <summary>
        /// Detects the feature set and caches it
        /// </summary>
        /// <param name="featureDetector"></param>
        /// <param name="image"></param>
        /// <returns></returns>
        public static FeatureSet Detect(KAZE featureDetector, Image<Gray, byte> image)
        {
            using (UMat uModelImage = image.ToUMat())
            {
                Mat descriptors = new Mat();
                var keyPoints = new VectorOfKeyPoint();
                featureDetector.DetectAndCompute(uModelImage, null, keyPoints, descriptors, false);

                return new FeatureSet()
                {
                    image = image,
                    descriptors = descriptors,
                    keyPoints = keyPoints,
                };
            }
        }

        public void Dispose()
        {
            image?.Dispose();
            image = null;

            descriptors?.Dispose();
            descriptors = null;

            keyPoints?.Dispose();
            keyPoints = null;
        }
    }
}
