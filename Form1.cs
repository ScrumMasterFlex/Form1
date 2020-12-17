// Garrick Beaster
// UR2 Term Project

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace WindowsFormsApp10
{
    public partial class Form1 : Form
    {
        VideoCapture _capture;
        Thread _captureThread;
        private object workingImage;
        private int chosen;
        SerialPort arduinoSerial = new SerialPort();
        bool enableCoordinateSending = true;
        Thread serialMonitoringThread;
        bool readyForNewShape = false;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // create the capture object and processing thread
            _capture = new VideoCapture(1);
            _captureThread = new Thread(ProcessImage);
            _captureThread.Start();

            try
            {
                arduinoSerial.PortName = "COM12";
                arduinoSerial.BaudRate = 115200;
                arduinoSerial.Open();
                serialMonitoringThread = new Thread(MonitorSerialData);
                serialMonitoringThread.Start();
                xInput.Text = "130";
                yInput.Text = "224";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error Initializing COM port");
                Close();
            }
        }


        private void ProcessImage()
        {
            while (_capture.IsOpened)
            {
                // frame maintenance
                Mat sourceFrame = _capture.QueryFrame();
                // resize to PictureBox aspect ratio
                int newHeight = sourceFrame.Size.Height * pictureBox1.Size.Width / sourceFrame.Size.Width;
                Size newSize = new Size(pictureBox1.Size.Width, newHeight);
                CvInvoke.Resize(sourceFrame, sourceFrame, newSize);
                Point targetPointForArduino = new Point();
                string targetShapeForArduino = "";

                // clone for all
                Invoke(new Action(() =>
                {
                    pictureBox1.Image = sourceFrame.Clone().Bitmap;
                }));

                // binary magic
                var binaryImage = sourceFrame.ToImage<Gray, byte>().ThresholdBinary(new Gray(100), new Gray(255)).Mat;

                // put some 
                var decoratedImage = new Mat();
                 
                CvInvoke.CvtColor(binaryImage, decoratedImage, typeof(Gray), typeof(Bgr));

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                    {
                        // Build list of contours
                        CvInvoke.FindContours(binaryImage, contours, null, RetrType.List,
                        ChainApproxMethod.ChainApproxSimple);
                        int shapesFound = 0;
                        int squaresFound = 0;
                        int trianglesFound = 0;
                        targetPointForArduino = new Point();

                    for (int i = 0; i < contours.Size; i++)
                    {

                        VectorOfPoint contour = contours[i];
                        double area = CvInvoke.ContourArea(contour);
                        if(area >= 1500 && area <= 6000)
                        {
                            shapesFound++;
                            Rectangle boundingBox = CvInvoke.BoundingRectangle(contour);
                            Point point = new Point(boundingBox.X + (boundingBox.Width / 2), boundingBox.Y + (boundingBox.Height / 2));
                            Point arduinoTarget = new Point(point.X / 22, point.Y / 17);
                            string shape = "";

                            if (area > 3200)
                            {

                                CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Red).MCvScalar);

                                CvInvoke.Circle(decoratedImage, point, 1, new Bgr(Color.Red).MCvScalar);

                                shape = "S";

                                squaresFound++;
                            }
                            else 
                            {

                                CvInvoke.Polylines(decoratedImage, contour, true, new Bgr(Color.Blue).MCvScalar);

                                CvInvoke.Circle(decoratedImage, point, 1, new Bgr(Color.Blue).MCvScalar);

                                shape = "T";

                                trianglesFound++;

                            }

                            if (shapesFound >= 1)
                            {
                                Invoke(new Action(() =>
                                {
                                    coordLabel.Text = $"coordinats {arduinoTarget.X},{arduinoTarget.Y} ";
                                    targetPointForArduino = point;
                                    targetShapeForArduino = shape;

                                }));
                            }
                        }

                        
                    }
                         

                    Invoke(new Action(() =>
                    {
                        contureLable.Text = $"There are {shapesFound} contours detected";

                        squaresLable.Text = $"There are {squaresFound} squares detected";

                        trianglesLabel.Text = $"There are {trianglesFound} triangles detected";
                    }));
                }
                    
                // output images:
                pictureBox2.Image = decoratedImage.Bitmap;

                // send coordinates to arduino if x & y do not equal zero
                if(readyForNewShape && targetPointForArduino.X != 0 && targetPointForArduino.Y != 0)
                {
                    // send coordinate
                    byte[] buffer = new byte[5] {
                        Encoding.ASCII.GetBytes("<")[0],
                        Convert.ToByte(targetPointForArduino.X),
                        Convert.ToByte(targetPointForArduino.Y),
                        Encoding.ASCII.GetBytes(targetShapeForArduino)[0],
                        Encoding.ASCII.GetBytes(">")[0]
                    };
                    arduinoSerial.Write(buffer, 0, 5);

                    // reset var to false:
                    readyForNewShape = false;
                }
                
            }

            
        }
        private void sendBtn_Click(object sender, EventArgs e) => readyForNewShape = true;
        private void MonitorSerialData()
        {
            while (true)
            {
                // block until \n character is received, extract command data
                string msg = arduinoSerial.ReadLine();
                // confirm the string has both < and > characters
                if (msg.IndexOf("<") == -1 || msg.IndexOf(">") == -1)
                {
                    continue;
                }
                
                // remove everything before (and including) the < character
                msg = msg.Substring(msg.IndexOf("<") + 1);
                // remove everything after (and including) the > character
                msg = msg.Remove(msg.IndexOf(">"));
                // if the resulting string is empty, disregard and move on

                if (msg.Length == 0)
                {
                    continue;
                }

                serialMonitoringThread.Abort();
               
                // parse the command
                if (msg.Substring(0, 1) == "S") //S for Send
                {
                    // command is to suspend, toggle states accordingly:
                    ToggleFieldAvailability(msg.Substring(1, 1) == "1");
                    if (msg.Substring(1, 1) == "1")
                    {
                        readyForNewShape = true;
                    }
                }
                else if (msg.Substring(0, 1) == "P") //P for Point
                {
                    // command is to display the point data, output to the text field:
                    Invoke(new Action(() =>
                    {
                        returnedPointLbl.Text = $"Returned Point Data: {msg.Substring(1)}";
                    }));
                }
            }
        }
        private void ToggleFieldAvailability(bool suspend)
        {
            Invoke(new Action(() =>
            {
                enableCoordinateSending = !suspend;
                lockStateToolStripStatusLabel.Text = $"State: {(suspend ? "Locked" : "Unlocked")}";
            }));




        }
        private void Form1_FormClosing_1(object sender, FormClosingEventArgs e)
        {
                    _captureThread.Abort();
            serialMonitoringThread.Abort();
        }

        
    }
}
