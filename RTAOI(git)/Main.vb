'TO DO


Option Strict Off


Imports System.Runtime.InteropServices
Imports System.Math
Imports System.Threading
Imports System.Linq
Imports System.Net    'TCP
Imports System.Buffer ' for saving binary files
Imports System.Text
Imports System.IO

Imports ManagedCuda
Imports ManagedCuda.CudaFFT
Imports System.Numerics
Imports ManagedCuda.VectorTypes
Imports ManagedCuda.CudaBlas
Imports BitMiracle.LibTiff.Classic






Public Class Main

    Dim TCPClients As Sockets.TcpClient  'this controls photostim pattern
    Dim TCPClientStream As Sockets.NetworkStream
    Dim fmt As String = "00"
    Dim SLMPatternFormat As String = "0000"
    Dim TCPConnected As Boolean = False
    Private AllResetFrames As Integer = 2  ' ignore the values in photostim frames

    'ROIs
    Private NumTriggerROIs As Integer = 1  ' number of trigger rois in experiment  20160410 set NumTriggerROIs = 3; not responding after ~ 50 frames at zoom2
    Private SelectedROIsCount As Integer = 0  'used for start, count numebr of rois as they are seleted
    Private MaskRadius As Integer = 10 ' useed 12 for zoom2
    'size in pixels of the radius of thecircle centred around trigger ROI centroid from which activity traces are extracted. was 9
    Private ROIMasks As New List(Of Image)
    Private ROIMaskIndices(NumTriggerROIs) As Queue(Of Integer)
    Private AllMaskROIArray(512 * 512 - 1) As Integer
    Private SumROIintensity(NumTriggerROIs - 1) As Integer

    Private ROIMaskIndicesX(NumTriggerROIs - 1) As Queue(Of Integer)
    Private ROIMaskIndicesY(NumTriggerROIs - 1) As Queue(Of Integer)
    Private ROICoordsX(NumTriggerROIs - 1) As Integer
    Private ROICoordsY(NumTriggerROIs - 1) As Integer
    Private ROICoords(NumTriggerROIs - 1) As Integer
    Private RefMagnification As Single = 1.14      'zoom used for generating SLM phase mask

    'startup bits
    Private SamplesReceived As Long
    Private ExperimentRunning As Boolean
    Private AllROIsSelected As Boolean
    Private TimeStarted As Long
    Private LockThresholds As Boolean
    Private StartRecording As Boolean = False
    Private NumActiveROIs As Integer = 0
    Private StimNum As Integer
    Private NumFrames As Integer = 0
    Private CurrentSLMPattern As Integer = 0 'keep track of SLM pattern to know how many time to trigger to get to desired position in list. not used
    Private BlankXSamplesPostStim As Integer = 0


    'data
    Private NumDisplays As Integer = 12
    Private threshLabel(NumDisplays - 1) As Label
    Private threshSpinbox(NumDisplays - 1) As NumericUpDown
    Private averageLabel(NumDisplays - 1) As Label
    Private averageValue(NumDisplays - 1) As Label
    Private FireThisWhenAboveCheck(NumDisplays - 1) As CheckBox
    Private FireThisWhenAboveLabel(NumDisplays - 1) As Label
    Private FireThisWhenBelowCheck(NumDisplays - 1) As CheckBox
    Private FireThisWhenBelowLabel(NumDisplays - 1) As Label
    Private SlidingWindowSize As Integer = 60 'in samples (frames), this is the size of the buffer holding the data (ROI traces)
    Private TempArrayForPlot(SlidingWindowSize - 1) As Double
    Private RoundTempArray(SlidingWindowSize - 1) As Double
    Private SlidingWindowArray(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single ' for uptodate baseline 
    Private ROIThreshold(NumTriggerROIs - 1) As Single
    Private SumPixelValue As Long
    Private NumPixelsForROI As Integer
    Private CurrentValue As Single
    Private CurrentRatio As Single
    Private LastRatio(NumTriggerROIs - 1) As Single
    Private CurrentAvgValue(NumTriggerROIs - 1) As Double ' average every AvgWindowSize frames
    Private PreviousAvgValue(NumTriggerROIs - 1) As Double
    Private AvgWindowSize As Integer = 3
    Private AvgWindowCount As Integer = 0
    Private RatioBuffer(NumTriggerROIs - 1, AvgWindowSize - 1) As Double ' for rolling average
    Private av As Single
    Private sd As Single
    Private NumExperiment As Integer = 1

    ' for baseline 
    Private ROIBaseline(NumTriggerROIs - 1) As Single
    Private SumBaseline(NumTriggerROIs - 1) As Single
    Private NumBaselineSamples As Integer = 0
    Private NumBaselineSpRequired As Integer = 150
    Private BaselineAquired As Boolean = False

    ' for photostim trigger
    Private AllTriggerStatus As Integer = 0  ' SLM pattern No.
    Private TriggerTable(NumTriggerROIs - 1) As Integer
    Private SumTriggerTable As Integer
    Private DisplayOn As Boolean = True

    'define closed loop frames
    Private TotalNumFrames As Integer = 4200 ' number of frames in control and closed-loop period
    Private TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1) As Boolean ' not used in sensory exp

    'for display 
    Private flipdone As Boolean = False
    Private AvgRGB(512 * 512 - 1) As Integer
    Private AvgRGBCount As Integer

    'daq output
    'Private daqtask As New Task
    Private daqtaskS1 As New Task  ' Dev6 ao0 toSensory (pink label)
    Private daqtaskS2 As New Task   ' Dev6 ao1 toPV (pink label)
    Private writerS1 As New AnalogSingleChannelWriter(daqtaskS1.Stream)
    Private writerS2 As New AnalogSingleChannelWriter(daqtaskS2.Stream)
    Private OutputArrayLength As Integer = 15 * NumTriggerROIs - 1
    Private OutputArray(1, OutputArrayLength) As Double
    Private ToPVreset(5) As Double
    Private ToPVtrigger(10) As Double
    Private ClampOnArray(5) As Double
    Private ClampOffArray(5) As Double
    Private TestOutputArray(1, 10) As Double
    Private TriggersToSLM(NumTriggerROIs, OutputArrayLength) As Double  'should be size of NumTriggerROIs, 8 works for now
    Private TriggerToPV(NumTriggerROIs, OutputArrayLength) As Double  'should be size of NumTriggerROIs, 8 works for now
    Private TriggerEnabled(NumTriggerROIs - 1) As Boolean
    Private AllTriggersEnabled As Boolean
    Private LastTrigger As Long
    Private LastTriFrame As Long
    Private LastTriggerOnFrame As Long  ' for single trigger, control experiment
    Private LastStimFrame(NumTriggerROIs - 1) As Integer

    ' record
    Private RecordArray(2, 12000) As String
    Private SaveFilePath As String = "F:\Data\Zoe\RTAOI notes"      'default save file path
    Private ReadFilePath As String

    ' daq input
    Private daqtask2 As New Task                               ' new ni task for reading ai
    Private reader As New AnalogSingleChannelReader(daqtask2.Stream)

    'Prairie View stream
    Private m_iImageSize As Integer = 512
    Private m_pl As New PrairieLink.Application
    Private m_oBitmap As New Bitmap(m_iImageSize, m_iImageSize, Imaging.PixelFormat.Format32bppRgb) 'was Format32bppArgb
    Private m_iTotalSamples As Integer = 0
    Private m_oUpdateThread As New Thread(AddressOf UpdateImage)
    Private m_closing As Boolean = False
    Private m_started As Boolean = False
    Private m_paused As Boolean = True
    Private m_flipOdd As Boolean = False
    Private m_flipEven As Boolean = False
    Private m_iBitsToShift As Integer = 6 ' 4 for NI, 6 for GS
    Private m_iSamplesPerPixel As Integer = 3
    Dim m_oTimer As New Stopwatch
    Private m_iProcessingIterations As Long = 0
    Private m_fProcessingTime As Long = 0
    Private m_iPollingIterations As Integer = 0
    Private m_fPollingTime As Long = 0
    Private m_iPollingIterations2 As Integer = 0
    Private m_fPollingTime2 As Long = 0
    Dim SamplesPerFrame As Integer = m_iImageSize * m_iImageSize * m_iSamplesPerPixel
    Dim iFrameSize As Integer = m_iImageSize * m_iImageSize

    ' activity clamp
    Private IsCalciumClamp As Boolean = False
    Private NumClampOnFrames As Integer = 900
    Private NumClamps As Integer = 1
    Private NumClampIntervalFrames As Integer = 900

    ' sensory stim
    Dim SensoryStimType As String = "1"
    Dim TCPClients2 As Sockets.TcpClient ' this controls sensory stim type
    Dim TCPClientStream2 As Sockets.NetworkStream
    Dim sendbytes2() As Byte = System.Text.Encoding.ASCII.GetBytes("1")
    Private serverConnected As Boolean = False
    Private IsMaster As Boolean = True
    Private IsSlave As Boolean = False
    Private IsSensoryStim As Boolean = False
    Private NumFramesPostStim As Integer = 0
    Private NumFramesPostSenStimRequired As Integer = 0 'delay between the end of sensory stim and photostim 
    Private SenStimDetected As Boolean = False
    Private SenStimStartFrame As Integer = 0
    Private RecordSenStimStarted(0, 200) As String
    Private NumSenStim As Integer = 0
    Private ToSensoryStim(5) As Double

    Private NumFramesInterStim As Integer = 390 ' was 300, changed on 20170921
    Private TotalSensoryStims As Integer = 15
    Private FlagROIAboveThresh(NumTriggerROIs - 1) As Boolean
    Private SenStimAllTriggerStatus As Integer = 0

    Private NumStimFrames As Integer = 3 'number of frames with sensory stim on -- used as the postframe to calculate sta 
    Private StaNumAvgFrames As Integer = 27
    Private StaDffSum(NumTriggerROIs - 1) As Double
    Private StaDff(NumTriggerROIs - 1) As Double
    Private SenClamp As Boolean = False
    Private NumSenClampFrames As Integer = 30
    Private NumStaFramesRecvd As Integer = 0
    Private FlagSenStimFinished As Boolean = False
    Private FlagSendTTLtrain As Boolean = False

    ' trigger-target experiment
    Private IsTrigTar As Boolean = False
    Private TrgCellIdx As Integer = 0
    Private SlidingAvg(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single
    Private SlidingStd(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single
    Private ROIav(NumTriggerROIs - 1) As Single
    Private ROIstd(NumTriggerROIs - 1) As Single
    Private LastTrigRatio(NumTriggerROIs - 1) As Single
    Private WaitFramesAfterTrig As Integer = 30
    Private N As Integer = 2
    Private tempidx(NumTriggerROIs - 1) As Integer ' for sliding window

    Private IsControl As Boolean = False
    Private RandomNum As Single = 0

    ' get noise std during control period
    Private NoiseStd(NumTriggerROIs - 1) As Single
    Private NumNoiseStdFrames(NumTriggerROIs - 1) As Integer
    Private NumControlFrames As Integer = 3000

    Private LastTriThresh(NumTriggerROIs - 1) As Integer
    Private NumPostStim(NumTriggerROIs - 1) As Integer


    'parameters for test  
    'Private ExperimentRunning As Boolean = True
    Private GetIntensityTime As Long = 0
    Private ThisFramePeriod As Long = 0
    Private TestPeriod As Long = 0
    Private TestPeriodArray(10000 - 1) As String
    Private DisplayOffPeriod As Integer = 5    ' number of frames every display-off period
    Private DisplayOffCounter As Integer = 0

    '' save to binary file
    Const INT_SIZE As Integer = 2
    Private IfSaveBinary As Boolean
    Private BinFileName As String
    Private fileStream As IO.FileStream
    Private NumByteWritten As Integer

    'playback 
    Private IsPlayBack As Boolean = False
    Private ReadStimFrames() As Integer
    Private ReadAllTriggerStatus() As Integer
    Private PlaybackCount As Integer = 0
    Private NumReadStimFrames As Integer

    'cuda 
    Dim IfUseCuda As Boolean = False
    Dim IfGetShifts As Boolean = False
    Dim gpuDviceID As Integer = 0

    Dim maxId As Integer = 0
    Dim shiftpx As Integer = 0
    Dim shiftx As Integer = 0
    Dim shifty As Integer = 0
    Dim shiftx_lb As Integer = 0
    Dim shifty_lb As Integer = 0
    Dim MatrixMulKernel As CudaKernel
    Dim AbsComplexKernel As CudaKernel
    Dim SampleMeanKernel As CudaKernel
    Dim int2complexKernel As CudaKernel

    Dim IfFlipEven As Integer = 1
    Dim MATRIX_SIZE As Integer = 512
    Dim TILE_SIZE As Integer = 32
    Dim BLOCK_SIZE As Integer = TILE_SIZE
    Dim GRID_SIZE As Integer = 16
    Dim ctx As CudaContext
    Dim plan2D As New CudaFFTPlan2D(512, 512, cufftType.C2C) 'fft plan
    Dim devSamples As New CudaDeviceVariable(Of Int16)(512 * 512 * 3)
    Dim devGrayPixels As New CudaDeviceVariable(Of Int32)(512 * 512)
    Dim devCurrentComplex As New CudaDeviceVariable(Of cuFloatComplex)(512 * 512)
    Dim devCurrentFloat As New CudaDeviceVariable(Of Single)(512 * 512)
    Dim ReadRefImgPath As String
    Dim RefImage As Bitmap
    Dim RefGrayPixels(512 * 512) As cuFloatComplex
    Dim devRefImageFFT As New CudaDeviceVariable(Of cuFloatComplex)(512 * 512)
    Dim FlagNewRefImageLoaded As Boolean = False

    Dim toHostTime As Long

    Dim processedGrayPixels(262144 - 1) As Integer
    Dim processedGrayPixels2Save(262144 - 1) As UInt16
    Private Sub OpenImage()
        'open an image to use for selection of ROIs
        OpenFileDialog1.Filter = "BMP |*.bmp| All Files|*.*"
        OpenFileDialog1.FileName = ""
        If OpenFileDialog1.ShowDialog(Me) = DialogResult.OK Then
            Dim ImgPath As String = OpenFileDialog1.FileName
            PictureBox1.Image = System.Drawing.Bitmap.FromFile(ImgPath)
        End If
    End Sub

    Private Sub zoomChange(ByVal Magnification As Single)     ' change the ROI size. cannot use zoom when display is off

        ' reset image
        PictureBox2.Image = Nothing
        Dim bmp0 As New Drawing.Bitmap(512, 512)
        PictureBox2.Image = bmp0

        For ROIIx As Integer = 0 To NumTriggerROIs - 1

            Dim bitshift As Single = (1 - Magnification) * 255
            Dim ThisROICoordsX As Single = Magnification * ROICoordsX(ROIIx) + bitshift
            Dim ThisROICoordsY As Single = Magnification * ROICoordsY(ROIIx) + bitshift
            Dim ThisMaskRadius As Single = MaskRadius * Magnification

            If (ThisROICoordsX + ThisMaskRadius) > 512 Or (ThisROICoordsX - ThisMaskRadius) < 0 Or (ThisROICoordsY + ThisMaskRadius) > 512 Or (ThisROICoordsY - ThisMaskRadius) < 0 Then
                MessageBox.Show("ROI out of image")
                PictureBox2.Invalidate()
                Exit Sub
            End If


            Using g As Graphics = Graphics.FromImage(PictureBox2.Image)

                Dim CustomPen As New Pen(Color.Red, 3)
                g.DrawEllipse(CustomPen, CInt(ThisROICoordsX - ThisMaskRadius), CInt(ThisROICoordsY - ThisMaskRadius), CInt(ThisMaskRadius * 2), CInt(ThisMaskRadius * 2))
                ' make bitmap of ROI mask (contour) - could also paint complex shapes
                Dim bmp As New Drawing.Bitmap(512, 512)
                Using g2 As Graphics = Graphics.FromImage(bmp)
                    g2.Clear(Drawing.Color.Black)
                    g2.FillEllipse(Brushes.White, CInt(ThisROICoordsX - ThisMaskRadius), CInt(ThisROICoordsY - ThisMaskRadius), CInt(ThisMaskRadius * 2), CInt(ThisMaskRadius * 2))
                End Using
                ROIMasks.Add(bmp)
                ' Get indices of selected pixels
                ROIMaskIndices(ROIIx) = New Queue(Of Integer)()
                ROIMaskIndicesX(ROIIx) = New Queue(Of Integer)()
                ROIMaskIndicesY(ROIIx) = New Queue(Of Integer)()
                For i As Integer = 0 To bmp.Width - 1
                    For j As Integer = 0 To bmp.Height - 1
                        If bmp.GetPixel(i, j).ToArgb() = Color.White.ToArgb() Then
                            ROIMaskIndices(ROIIx).Enqueue((j * 512) + i)
                            ROIMaskIndicesX(ROIIx).Enqueue(i)
                            ROIMaskIndicesY(ROIIx).Enqueue(j)
                        End If
                    Next
                Next


            End Using

        Next
        PictureBox2.Invalidate()


    End Sub

    Private Sub pictureBox2_MouseDown(ByVal sender As Object, ByVal e As MouseEventArgs) Handles PictureBox2.MouseDown
        'called when user clicks on fov image.
        'makes the ROI masks, and also draws their location of screen

        Using g As Graphics = Graphics.FromImage(PictureBox2.Image)
            If SelectedROIsCount < NumTriggerROIs Then
                ROICoordsX(SelectedROIsCount) = e.X
                ROICoordsY(SelectedROIsCount) = e.Y
                ROICoords(SelectedROIsCount) = (e.Y * 512) + e.X

                Dim CustomPen As New Pen(Color.Red, 3)
                g.DrawEllipse(CustomPen, e.X - MaskRadius, e.Y - MaskRadius, MaskRadius * 2, MaskRadius * 2)

                Dim MyBrush As New SolidBrush(Color.Red)
                Dim StringFont As New Font("Arial", 10)
                g.DrawString(CStr(SelectedROIsCount + 1), StringFont, MyBrush, e.X - MaskRadius - 5, e.Y - MaskRadius - 5)

                ' make bitmap of ROI mask (contour) - could also paint complex shapes
                Dim bmp As New Drawing.Bitmap(512, 512)
                Using g2 As Graphics = Graphics.FromImage(bmp)
                    g2.Clear(Drawing.Color.Black)
                    g2.FillEllipse(Brushes.White, e.X - MaskRadius, e.Y - MaskRadius, MaskRadius * 2, MaskRadius * 2)
                End Using
                ROIMasks.Add(bmp)

                ' Get indices of selected pixels
                For i As Integer = 0 To bmp.Width - 1
                    For j As Integer = 0 To bmp.Height - 1
                        If bmp.GetPixel(i, j).ToArgb() = Color.White.ToArgb() Then
                            ROIMaskIndices(SelectedROIsCount).Enqueue((j * 512) + i)
                            ROIMaskIndicesX(SelectedROIsCount).Enqueue(i)
                            ROIMaskIndicesY(SelectedROIsCount).Enqueue(j)
                            AllMaskROIArray((j * 512) + i) = SelectedROIsCount + 1
                        End If
                    Next
                Next

                SelectedROIsCount += 1
                If SelectedROIsCount = NumTriggerROIs Then
                    StatusText.Text = "All ROIs selected."
                    AllROIsSelected = True
                    TimeStarted = m_oTimer.ElapsedMilliseconds

                    PictureBox1.Size = New Size(m_iImageSize, m_iImageSize)
                    PictureBox1.Image = m_oBitmap

                Else
                    StatusText.Text = "Select " & Convert.ToString(NumTriggerROIs - SelectedROIsCount) & " trigger ROI(s)"
                End If

            End If
        End Using
        lbl_xpos.Text = e.X.ToString
        lbl_ypos.Text = e.Y.ToString
        PictureBox2.Invalidate()  'redraw image
    End Sub

    Private Sub Form1_FormClosing(ByVal sender As Object, ByVal e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        m_closing = True
        If Not m_oUpdateThread.Join(2000) Then
            m_oUpdateThread.Abort()
        End If
        m_pl.Disconnect()
    End Sub

    Private Sub Form1_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load

        'Is called on application start
        TboxAqNum.Text = CStr(NumExperiment)

        'Load an image for the FOV
        OpenImage()

        ' set up picture boxes (for FOV and ROI display)
        PictureBox2.Parent = PictureBox1
        PictureBox2.Location = New Point(0, 0)
        PictureBox2.BackColor = Color.Transparent
        Dim bmp As New Drawing.Bitmap(512, 512)
        PictureBox2.Image = bmp
        StatusText.Text = "Select " & Convert.ToString(NumTriggerROIs - SelectedROIsCount) & " trigger ROI(s)"

        'set up table showing sta values
        STA_DataGridView.ColumnCount = 2
        With STA_DataGridView
            .RowHeadersVisible = False
            .ColumnHeadersVisible = False
        End With

        ' initialise ROI masks
        For i As Integer = 0 To NumTriggerROIs - 1
            'Initialise ROI mask indices array(queue because unknown size)
            ROIMaskIndices(i) = New Queue(Of Integer)()
            ROIMaskIndicesX(i) = New Queue(Of Integer)()
            ROIMaskIndicesY(i) = New Queue(Of Integer)()
        Next

        ' add components
        AddROIComponents()


        ' monitor all rois
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerEnabled(i) = True
        Next

        '================= build TTL triggers =============== 
        '----- 6 ms  -----
        For i As Integer = 0 To 5
            ToPVreset(i) = 0
            ClampOnArray(i) = 5
            ClampOffArray(i) = 0
        Next
        '----- 10 ms  -----
        For i As Integer = 0 To 9
            ToPVtrigger(i) = 5
        Next
        '----- 5 ms  -----
        For i As Integer = 0 To 4
            ToSensoryStim(i) = 5
        Next

        '==================== build trial status =================================

        ''-- for multi-cell stimulation
        For i As Integer = NumControlFrames To TotalNumFrames - 1
            TrialStatus(i) = True
        Next

        ''-- for trigger-target control experiments --
        ' Initialize the random-number generator.
        Randomize()

        ' for multi-cell stimulation
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerTable(i) = Math.Pow(2, i)
            SumTriggerTable += TriggerTable(i)
        Next


        ' DAQ configuration
        daqtaskS1.AOChannels.CreateVoltageChannel("Dev6/ao0", "", -10.0, 10.0, AOVoltageUnits.Volts)
        daqtaskS2.AOChannels.CreateVoltageChannel("Dev6/ao1", "", -10.0, 10.0, AOVoltageUnits.Volts)
        daqtaskS1.Timing.SampleClockRate = 10000
        daqtaskS2.Timing.SampleClockRate = 10000
        daqtask2.AIChannels.CreateVoltageChannel("Dev6/ai1", "readChannel", AITerminalConfiguration.Differential, 0.0, 5.0, AIVoltageUnits.Volts)

        'connect to prairie view  
        m_pl.Connect()
        m_pl.SendScriptCommands("-dw")
        m_pl.SendScriptCommands("-lbs true 0")
        m_pl.SendScriptCommands("-srd True 0") ' was 25; changed on 20170918 to grab only one frame


        m_started = True
        m_paused = Not m_paused


        chkFlipOdd.Checked = True

        'flush 
        Dim iSamples As Integer = 1
        While iSamples > 0
            Dim iBuffer() As Int16 = m_pl.ReadRawDataStream(iSamples)
        End While

        'start thread
        m_oUpdateThread.Priority = ThreadPriority.AboveNormal
        m_oUpdateThread.Start()



    End Sub

    Private Sub AddROIComponents()

        ' add Roi plots to gui
        For i As Integer = 0 To NumDisplays - 1
            'Set x limit of graphs
            Dim offset As Integer = 380
            If i < 4 Then
                Chart1.ChartAreas(i).AxisX.Maximum = SlidingWindowSize

                'Add some components to gui
                threshLabel(i) = New Label
                threshLabel(i).Text = "Threshold"
                threshLabel(i).Size = New Size(80, 13)
                threshLabel(i).Left = 980
                threshLabel(i).Top = 30 + (i * 125)
                threshSpinbox(i) = New NumericUpDown
                threshSpinbox(i).Maximum = 999999
                threshSpinbox(i).Minimum = -999999
                threshSpinbox(i).Tag = i
                AddHandler threshSpinbox(i).ValueChanged, AddressOf ThresholdValueChanged
                threshSpinbox(i).Value = 0
                threshSpinbox(i).Size = New Size(80, 13)
                threshSpinbox(i).Left = 980
                threshSpinbox(i).Top = 50 + (i * 125)
                threshSpinbox(i).DecimalPlaces = 2
                averageLabel(i) = New Label
                averageLabel(i).Text = "Average"
                averageLabel(i).Size = New Size(80, 13)
                averageLabel(i).Left = 980
                averageLabel(i).Top = 70 + (i * 125)
                averageValue(i) = New Label
                averageValue(i).Text = "0"
                averageValue(i).Size = New Size(80, 13)
                averageValue(i).Left = 980
                averageValue(i).Top = 90 + (i * 125)
                FireThisWhenAboveCheck(i) = New CheckBox
                FireThisWhenAboveCheck(i).Left = 1020
                FireThisWhenAboveCheck(i).Top = 105 + (i * 125)
                FireThisWhenAboveLabel(i) = New Label
                FireThisWhenAboveLabel(i).Left = 980
                FireThisWhenAboveLabel(i).Top = 110 + (i * 125)
                FireThisWhenAboveLabel(i).Text = "Above"
                FireThisWhenBelowCheck(i) = New CheckBox
                FireThisWhenBelowCheck(i).Left = 1080
                FireThisWhenBelowCheck(i).Top = 105 + (i * 125)
                FireThisWhenBelowLabel(i) = New Label
                FireThisWhenBelowLabel(i).Left = 1040
                FireThisWhenBelowLabel(i).Top = 110 + (i * 125)
                FireThisWhenBelowLabel(i).Text = "Below"
            ElseIf i < 8 Then
                Dim j As Integer = i - 4
                Chart2.ChartAreas(j).AxisX.Maximum = SlidingWindowSize

                'Add some components to gui
                threshLabel(i) = New Label
                threshLabel(i).Text = "Threshold"
                threshLabel(i).Size = New Size(80, 13)
                threshLabel(i).Left = threshLabel(j).Left + offset
                threshLabel(i).Top = 30 + (j * 125)
                threshSpinbox(i) = New NumericUpDown
                threshSpinbox(i).Maximum = 999999
                threshSpinbox(i).Minimum = -999999
                threshSpinbox(i).Tag = i
                AddHandler threshSpinbox(i).ValueChanged, AddressOf ThresholdValueChanged
                threshSpinbox(i).Value = 0
                threshSpinbox(i).Size = New Size(80, 13)
                threshSpinbox(i).Left = threshSpinbox(j).Left + offset
                threshSpinbox(i).Top = 50 + (j * 125)
                threshSpinbox(i).DecimalPlaces = 2
                averageLabel(i) = New Label
                averageLabel(i).Text = "Average"
                averageLabel(i).Size = New Size(80, 13)
                averageLabel(i).Left = averageLabel(j).Left + offset
                averageLabel(i).Top = 70 + (j * 125)
                averageValue(i) = New Label
                averageValue(i).Text = "0"
                averageValue(i).Size = New Size(80, 13)
                averageValue(i).Left = averageValue(j).Left + offset
                averageValue(i).Top = 90 + (j * 125)
                FireThisWhenAboveCheck(i) = New CheckBox
                FireThisWhenAboveCheck(i).Left = FireThisWhenAboveCheck(j).Left + offset
                FireThisWhenAboveCheck(i).Top = 105 + (j * 125)
                FireThisWhenAboveLabel(i) = New Label
                FireThisWhenAboveLabel(i).Left = FireThisWhenAboveLabel(j).Left + offset
                FireThisWhenAboveLabel(i).Top = 110 + (j * 125)
                FireThisWhenAboveLabel(i).Text = "Above"
                FireThisWhenBelowCheck(i) = New CheckBox
                FireThisWhenBelowCheck(i).Left = FireThisWhenBelowCheck(j).Left + offset
                FireThisWhenBelowCheck(i).Top = 105 + (j * 125)
                FireThisWhenBelowLabel(i) = New Label
                FireThisWhenBelowLabel(i).Left = FireThisWhenBelowLabel(j).Left + offset
                FireThisWhenBelowLabel(i).Top = 110 + (j * 125)
                FireThisWhenBelowLabel(i).Text = "Below"
            ElseIf i < 12 Then
                offset *= 2
                Dim j As Integer = i - 8
                Chart3.ChartAreas(j).AxisX.Maximum = SlidingWindowSize

                'Add some components to gui
                threshLabel(i) = New Label
                threshLabel(i).Text = "Threshold"
                threshLabel(i).Size = New Size(80, 13)
                threshLabel(i).Left = threshLabel(j).Left + offset
                threshLabel(i).Top = 30 + (j * 125)
                threshSpinbox(i) = New NumericUpDown
                threshSpinbox(i).Maximum = 999999
                threshSpinbox(i).Minimum = -999999
                threshSpinbox(i).Tag = i
                AddHandler threshSpinbox(i).ValueChanged, AddressOf ThresholdValueChanged
                threshSpinbox(i).Value = 0
                threshSpinbox(i).Size = New Size(80, 13)
                threshSpinbox(i).Left = threshSpinbox(j).Left + offset
                threshSpinbox(i).Top = 50 + (j * 125)
                threshSpinbox(i).DecimalPlaces = 2
                averageLabel(i) = New Label
                averageLabel(i).Text = "Average"
                averageLabel(i).Size = New Size(80, 13)
                averageLabel(i).Left = averageLabel(j).Left + offset
                averageLabel(i).Top = 70 + (j * 125)
                averageValue(i) = New Label
                averageValue(i).Text = "0"
                averageValue(i).Size = New Size(80, 13)
                averageValue(i).Left = averageValue(j).Left + offset
                averageValue(i).Top = 90 + (j * 125)
                FireThisWhenAboveCheck(i) = New CheckBox
                FireThisWhenAboveCheck(i).Left = FireThisWhenAboveCheck(j).Left + offset
                FireThisWhenAboveCheck(i).Top = 105 + (j * 125)
                FireThisWhenAboveLabel(i) = New Label
                FireThisWhenAboveLabel(i).Left = FireThisWhenAboveLabel(j).Left + offset
                FireThisWhenAboveLabel(i).Top = 110 + (j * 125)
                FireThisWhenAboveLabel(i).Text = "Above"
                FireThisWhenBelowCheck(i) = New CheckBox
                FireThisWhenBelowCheck(i).Left = FireThisWhenBelowCheck(j).Left + offset
                FireThisWhenBelowCheck(i).Top = 105 + (j * 125)
                FireThisWhenBelowLabel(i) = New Label
                FireThisWhenBelowLabel(i).Left = FireThisWhenBelowLabel(j).Left + offset
                FireThisWhenBelowLabel(i).Top = 110 + (j * 125)
                FireThisWhenBelowLabel(i).Text = "Below"
            End If


            Me.Controls.Add(threshLabel(i))
            Me.Controls.Add(threshSpinbox(i))
            Me.Controls.Add(averageLabel(i))
            Me.Controls.Add(averageValue(i))
            Me.Controls.Add(FireThisWhenBelowCheck(i))
            Me.Controls.Add(FireThisWhenBelowLabel(i))
            Me.Controls.Add(FireThisWhenAboveCheck(i))
            Me.Controls.Add(FireThisWhenAboveLabel(i))

        Next
    End Sub


    Private Sub UpdateImage()

        ctx = New CudaContext(gpuDviceID, True)
        plan2D = New CudaFFTPlan2D(512, 512, cufftType.C2C) 'fft plan
        'load kernels
        Dim GRID_DIM As New dim3(GRID_SIZE, GRID_SIZE, 1)
        Dim BLOCK_DIM As New dim3(TILE_SIZE, TILE_SIZE, 1)
        MatrixMulKernel = ctx.LoadKernel("kernel_x64.ptx", "MatrixMulKernel")
        MatrixMulKernel.GridDimensions = GRID_DIM
        MatrixMulKernel.BlockDimensions = BLOCK_DIM
        AbsComplexKernel = ctx.LoadKernel("kernel_x64.ptx", "abs_of_complex")
        AbsComplexKernel.GridDimensions = GRID_DIM
        AbsComplexKernel.BlockDimensions = BLOCK_DIM
        SampleMeanKernel = ctx.LoadKernel("kernel_x64.ptx", "sample_mean")
        SampleMeanKernel.BlockDimensions = New dim3(1024, 1)
        SampleMeanKernel.GridDimensions = New dim3(768, 1)
        int2complexKernel = ctx.LoadKernel("kernel_x64.ptx", "int2complex")
        int2complexKernel.GridDimensions = GRID_DIM
        int2complexKernel.BlockDimensions = BLOCK_DIM

        devSamples = New CudaDeviceVariable(Of Int16)(512 * 512 * 3)
        devGrayPixels = New CudaDeviceVariable(Of Int32)(512 * 512)
        devRefImageFFT = New CudaDeviceVariable(Of cuFloatComplex)(512 * 512)
        devCurrentComplex = New CudaDeviceVariable(Of cuFloatComplex)(512 * 512)

        devCurrentFloat = New CudaDeviceVariable(Of Single)(512 * 512)
        Dim devResultPixels = New CudaDeviceVariable(Of Int32)(512 * 512)
        Dim devTempComplex = New CudaDeviceVariable(Of cuFloatComplex)(512 * 512)
        RefGrayPixels(512 * 512) = New cuFloatComplex

        Dim cuBlasHd As CudaBlasHandle
        CudaBlasNativeMethods.cublasCreate_v2(cuBlasHd)
        m_oTimer.Start()
        Dim iLastElapsedMilliseconds As Long = 0
        Dim iPollStart As Long

        While Not m_closing

            While m_paused
                ' do nothing

            End While

            If FlagNewRefImageLoaded = True Then
                devRefImageFFT.CopyToDevice(RefGrayPixels)
                plan2D.Exec(devRefImageFFT.DevicePointer, TransformDirection.Forward)
                FlagNewRefImageLoaded = False
            End If

            Dim iSamples As Integer
            iPollStart = m_oTimer.ElapsedMilliseconds

            Dim iBuffer() As Int16 = m_pl.ReadRawDataStream(iSamples)     'when isamples > 3*512*512  error at marshal.copy!! 20160515

            ' only allow processing single frames
            If iSamples <> 786432 Then Continue While 'force it to be one frame

            ' If iSamples = 0 Then Continue While

            m_fPollingTime2 += m_oTimer.ElapsedMilliseconds - iPollStart
            m_iPollingIterations2 += 1


            If (IfUseCuda) Then
                ' takes less than 1 ms to copy to/from device
                Dim startCopy As Long = m_oTimer.ElapsedMilliseconds
                devSamples.CopyToDevice(iBuffer)
                SampleMeanKernel.Run(devSamples.DevicePointer, m_iImageSize, m_iImageSize, m_iSamplesPerPixel, IfFlipEven, devResultPixels.DevicePointer)

                If (IfGetShifts) Then
                    SampleMeanKernel.Run(devSamples.DevicePointer, m_iImageSize, m_iImageSize, m_iSamplesPerPixel, IfFlipEven, devGrayPixels.DevicePointer)
                    int2complexKernel.Run(devCurrentComplex.DevicePointer, devGrayPixels.DevicePointer)
                    plan2D.Exec(devCurrentComplex.DevicePointer, TransformDirection.Forward)
                    MatrixMulKernel.Run(devCurrentComplex.DevicePointer, devRefImageFFT.DevicePointer, devTempComplex.DevicePointer)

                    plan2D.Exec(devTempComplex.DevicePointer, TransformDirection.Inverse)
                    AbsComplexKernel.Run(devTempComplex.DevicePointer, devCurrentFloat.DevicePointer)

                    ' find maximum
                    Dim cuBlasSTAT = CudaBlasNativeMethods.cublasIsamax_v2(cuBlasHd, 512 * 512, devCurrentFloat.DevicePointer, 1, maxId)
                    shiftx = maxId Mod 512
                    shifty = maxId / 512

                    If shifty > 256 Then
                        shifty -= 512
                    End If

                    shifty_lb = shifty

                    shiftpx = 512 * shifty + shiftx

                    If shiftx > 256 Then
                        shiftx -= 512
                    End If
                    shiftx_lb = shiftx



                    If (Abs(shiftx) > 100 Or Abs(shifty) > 100) Then
                        shiftpx = 0
                    End If

                End If
                devResultPixels.CopyToHost(processedGrayPixels)
                toHostTime = m_oTimer.ElapsedMilliseconds - startCopy
            End If


            Me.BeginInvoke(New RefreshImageDelegate(AddressOf RefreshImage), New Object() {iBuffer, iSamples})

            Dim iElapsedMilliseconds As Long = m_oTimer.ElapsedMilliseconds

            m_fPollingTime += iElapsedMilliseconds - iLastElapsedMilliseconds
            iLastElapsedMilliseconds = iElapsedMilliseconds
            m_iPollingIterations += 1

        End While
    End Sub

    Private Delegate Sub RefreshImageDelegate(ByRef buffer As Int16(), ByVal samples As Integer)
    Private Sub RefreshImage(ByRef buffer As Int16(), ByVal samples As Integer)
        NumFrames += 1
        FrameNumber.Text = CStr(NumFrames)

        AvgWindowCount += 1
        If AvgWindowCount = AvgWindowSize Then
            AvgWindowCount = 0
        End If

        Static iLastElapsedMilliseconds As Long = 0

        ' commented from here 20171008
        Dim iExtraSamples = Max(0, (samples \ SamplesPerFrame) - 1) * SamplesPerFrame  ' don't bother processing multiple frames for one display update
        Dim iDataOffset As Integer = iExtraSamples
        ' end commment 20171008




        '********************************************************************************************************************
        '                                     GET ROI AVG INTENSITY FROM BUFFER    
        '********************************************************************************************************************
        Dim teststart As Long = m_oTimer.ElapsedMilliseconds
        '==================================             Display on             ==============================================
        If DisplayOn = True Or (DisplayOn = False And IfUseCuda = True) Or (DisplayOffCounter = DisplayOffPeriod - 1 And ExperimentRunning = False) Then   ' uncomment this for experiment
            Dim iSamplesToWrite As Integer
            If (Not IfUseCuda) Then
                ProcessGrayPixelsFullFrame(buffer, iExtraSamples, samples - iExtraSamples, processedGrayPixels) '20171008 commented
                iSamplesToWrite = processedGrayPixels.Length


                Dim Img_iExtraSamples As Integer = Max(0, (iSamplesToWrite \ iFrameSize) - 1) * iFrameSize  ' don't bother processing multiple frames for one display update
                iSamplesToWrite -= Img_iExtraSamples
                iDataOffset = Img_iExtraSamples

                'flip the rows, correct for bidirectional scanning
                If m_flipOdd OrElse m_flipEven Then
                    Dim iTemp As Integer
                    Dim iFlipOffset = iDataOffset
                    Dim iLeftoverSamples As Integer = m_iTotalSamples Mod m_iImageSize
                    If iLeftoverSamples > 0 Then iFlipOffset += m_iImageSize - iLeftoverSamples ' skipping partial lines to make this easier (resonant mode always provides full lines)
                    Dim odd As Boolean = (m_iTotalSamples \ m_iImageSize) Mod 2 = 0
                    While iFlipOffset <= processedGrayPixels.Length - m_iImageSize
                        If (m_flipOdd AndAlso odd) OrElse (m_flipEven AndAlso Not odd) Then
                            For iIndex As Integer = iFlipOffset To iFlipOffset + (m_iImageSize \ 2) - 1
                                iTemp = processedGrayPixels(iIndex)
                                processedGrayPixels(iIndex) = processedGrayPixels(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset))
                                processedGrayPixels(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset)) = iTemp
                            Next
                        End If
                        odd = Not odd
                        iFlipOffset += m_iImageSize
                    End While
                End If
            Else
                iSamplesToWrite = processedGrayPixels.Length
            End If

            '' get average image  --- display average of 16 frames
            For i As Integer = 0 To processedGrayPixels.Length - 1
                AvgRGB(i) += processedGrayPixels(i)
            Next
            AvgRGBCount += 1

            If (AvgRGBCount = 16) Then
                Dim displayIMG() As Integer = ConvertAvgGrayToRGB(AvgRGB, AvgRGBCount)
                Dim oData As Imaging.BitmapData = m_oBitmap.LockBits(New Rectangle(0, 0, m_iImageSize, m_iImageSize), Imaging.ImageLockMode.ReadWrite, Imaging.PixelFormat.Format32bppRgb)
                While iSamplesToWrite > 0
                    Dim iSamplesToCopy As Integer = Min(iSamplesToWrite, iFrameSize - m_iTotalSamples)
                    If iSamplesToCopy > 0 Then
                        Marshal.Copy(displayIMG, iDataOffset, New IntPtr(oData.Scan0.ToInt64 + m_iTotalSamples * 4), iSamplesToCopy)
                        m_iTotalSamples += iSamplesToCopy
                        If m_iTotalSamples >= iFrameSize Then m_iTotalSamples -= iFrameSize
                        iSamplesToWrite -= iSamplesToCopy
                        iDataOffset += iSamplesToCopy
                    End If
                End While

                m_oBitmap.UnlockBits(oData)
                AvgRGBCount = 0
                ReDim AvgRGB(processedGrayPixels.Length - 1)
            End If

        End If
        '================================== End of display on =================================================

        If DisplayOn = False Then ' test direct processing on buffer
            'flip the rows, correct for bidirectional scanning    flip ROI masks; only need to do this once
            If flipdone = False Then
                If m_flipOdd OrElse m_flipEven Then
                    Dim iTemp As Integer
                    Dim iFlipOffset = 0
                    Dim iLeftoverSamples As Integer = m_iTotalSamples Mod m_iImageSize
                    If iLeftoverSamples > 0 Then iFlipOffset += m_iImageSize - iLeftoverSamples ' skipping partial lines to make this easier (resonant mode always provides full lines)
                    Dim odd As Boolean = (m_iTotalSamples \ m_iImageSize) Mod 2 = 0
                    While iFlipOffset <= AllMaskROIArray.Length - m_iImageSize
                        If (m_flipOdd AndAlso odd) OrElse (m_flipEven AndAlso Not odd) Then
                            For iIndex As Integer = iFlipOffset To iFlipOffset + (m_iImageSize \ 2) - 1
                                iTemp = AllMaskROIArray(iIndex)
                                AllMaskROIArray(iIndex) = AllMaskROIArray(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset))
                                AllMaskROIArray(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset)) = iTemp
                            Next
                        End If
                        odd = Not odd
                        iFlipOffset += m_iImageSize
                    End While
                End If
                flipdone = True
            End If

            If IfSaveBinary Then
                ProcessGray16forSave(buffer, iExtraSamples, SamplesPerFrame, AllMaskROIArray, processedGrayPixels2Save)
            Else
                ProcessGray16(buffer, iExtraSamples, SamplesPerFrame, AllMaskROIArray, processedGrayPixels) ' changed  (samples - iExtraSamples) to SamplesPerFrame 20171008
            End If


        End If   ' end if displayon is false

        '  ************************            END  GET ROI AVG INTENSITY FROM BUFFER             ***********************************

        Dim teststop As Long = m_oTimer.ElapsedMilliseconds ' start timer for processing time 
        GetIntensityTime = teststop - teststart
        GetIntensityTimeLabel.Text = CStr(GetIntensityTime)

        'Here is where the realtime threshold checks and feedback/triggering happens:
        If AllROIsSelected Then
            '================================= GET BASELINE ============================================
            If NumBaselineSamples < NumBaselineSpRequired And NumFrames > 149 Then         ' skip first 149 frames
                If DisplayOn = True Then
                    For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                        NumPixelsForROI = ROIMaskIndices(ROIIdx).Count
                        For PixelIdx As Integer = 0 To NumPixelsForROI
                            SumBaseline(ROIIdx) += processedGrayPixels(ROIMaskIndices(ROIIdx)(PixelIdx))
                        Next
                    Next
                Else
                    For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                        SumBaseline(ROIIdx) += SumROIintensity(ROIIdx)
                    Next
                End If
                NumBaselineSamples += 1
            End If

            If NumBaselineSamples = NumBaselineSpRequired Then  ' get baseline

                For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                    ROIBaseline(ROIIdx) = SumBaseline(ROIIdx) / NumBaselineSpRequired
                    If DisplayOn = False Then
                        LastRatio(ROIIdx) = SumROIintensity(ROIIdx)
                    End If

                Next
                NumBaselineSamples += 1
                BaselineAquired = True
                AllTriggersEnabled = True

                '--------------------  Send sensory trigger --------------------
                If IsSensoryStim = True AndAlso IsMaster AndAlso NumSenStim <= TotalSensoryStims Then
                    writerS1.BeginWriteMultiSample(True, ToSensoryStim, Nothing, Nothing) 'write to channel a00

                    If FlagSendTTLtrain Then
                        writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01
                    End If

                    If serverConnected AndAlso IsPlayBack = False Then
                        TCPClients2.Client.Send(sendbytes2)
                    End If

                    NumSenStim += 1
                End If

                If NumSenStim = TotalSensoryStims + 1 Then
                    FlagSenStimFinished = True
                End If


            End If

            '============================= Compute F and threshold   ===================================
            If BaselineAquired Then
                SamplesReceived += 1
                '------------------ SENSORY STIMULATION ---------------------------
                ' RTAOI as slave; triggers (for sensory-on) should > 66 ms duration 
                If IsSensoryStim = True Then
                    If IsMaster Then
                        SenStimDetected = True
                    End If
                    '  AllTriggersEnabled = False
                    If SenStimDetected = False Then
                        Dim readerData As Double = reader.ReadSingleSample
                        lblAiVoltage.Text = Format(readerData, "0.00")
                        ' detect sensory stim trigger 
                        If readerData > 1 Then
                            SenStimDetected = True
                            NumFramesPostStim = 0
                            SenStimStartFrame = NumFrames
                            RecordSenStimStarted(0, NumSenStim) = SenStimStartFrame
                            NumSenStim += 1
                        End If
                    Else
                        NumFramesPostStim += 1
                    End If

                    If NumFramesPostStim > NumFramesInterStim Then 'update baseline
                        BaselineAquired = False
                        SenStimDetected = False
                        NumBaselineSamples = 0
                        NumFramesPostStim = 0
                        ReDim SumBaseline(NumTriggerROIs - 1)
                    End If
                    lblNoFramesPostStim.Text = CStr(NumFramesPostStim)
                    lblNumSenStim.Text = CStr(NumSenStim)
                End If
                '------------------------------------------------------------------------

                If (IfUseCuda = True Or DisplayOn = False) And AllTriggersEnabled = True Then
                    NumActiveROIs = 0 'can be used for adjusting photostim power
                    AllTriggerStatus = 0

                    If IfUseCuda = True Then
                        For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                            SumROIintensity(ROIIdx) = 0
                            For PixelIdx As Integer = 0 To ROIMaskIndices(ROIIdx).Count - 1
                                SumROIintensity(ROIIdx) += processedGrayPixels(ROIMaskIndices(ROIIdx)(PixelIdx) + shiftpx)
                            Next
                        Next
                    End If

                    ' ==============================  Trigger targets logic ===============================

                    If IsTrigTar = True Then
                        For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                            NumPostStim(ROIIdx) += 1

                            Dim CurrentStd As Double
                            ' -------  Get current av and std -------------
                            ROIav(ROIIdx) -= SlidingAvg(ROIIdx, tempidx(ROIIdx))
                            SlidingAvg(ROIIdx, tempidx(ROIIdx)) = SumROIintensity(ROIIdx) / SlidingWindowSize
                            ROIav(ROIIdx) += SlidingAvg(ROIIdx, tempidx(ROIIdx))
                            If CheckBox_UseNoise.Checked = True Then
                                ROIThreshold(ROIIdx) = ROIav(ROIIdx) + N * NoiseStd(ROIIdx)
                            Else
                                ROIstd(ROIIdx) -= SlidingStd(ROIIdx, tempidx(ROIIdx))
                                SlidingStd(ROIIdx, tempidx(ROIIdx)) = SumROIintensity(ROIIdx) ^ 2
                                ROIstd(ROIIdx) += SlidingStd(ROIIdx, tempidx(ROIIdx))
                                CurrentStd = Math.Sqrt((ROIstd(ROIIdx) - SlidingWindowSize * ROIav(ROIIdx) ^ 2) / (SlidingWindowSize - 1))
                                ROIThreshold(ROIIdx) = ROIav(ROIIdx) + N * CurrentStd
                            End If
                            tempidx(ROIIdx) += 1
                            If tempidx(ROIIdx) = SlidingWindowSize Then
                                tempidx(ROIIdx) = 0
                            End If
                            CurrentRatio = SumROIintensity(ROIIdx)
                            '=====get noise std start =============
                            If (TrialStatus(NumFrames - 1) = False) Then
                                NoiseStd(ROIIdx) += Abs(CurrentRatio - LastRatio(ROIIdx))
                                NumNoiseStdFrames(ROIIdx) += 1
                                LastRatio(ROIIdx) = CurrentRatio
                            End If
                            If NumFrames = NumControlFrames Then
                                NoiseStd(ROIIdx) = NoiseStd(ROIIdx) / NumNoiseStdFrames(ROIIdx)
                                lblNoiseStd.Text = CStr(NoiseStd(0)) ' & " " & CStr(NoiseStd(1)) & " " & CStr(NoiseStd(2)) & " " & CStr(NoiseStd(3))
                            End If
                            '=====end of get noise std =============
                            lblCurrentThresh.Text = Format(ROIThreshold(ROIIdx), "0.0")
                            If CheckBoxNoiseConstraint.Checked = True AndAlso (TrialStatus(NumFrames - 1) = True) AndAlso (CurrentStd < NoiseStd(ROIIdx)) Then
                                Continue For
                            End If
                            '-------- end get current av and std -----------

                            ' ---- enable photostim trigger after WaitFramesAfterTrig frames
                            If TriggerEnabled(ROIIdx) = False AndAlso NumPostStim(ROIIdx) > WaitFramesAfterTrig Then
                                TriggerEnabled(ROIIdx) = True
                            End If

                            '------- 
                            If FireThisWhenAboveCheck(ROIIdx).Checked = True And CurrentRatio > ROIThreshold(ROIIdx) And TriggerEnabled(ROIIdx) = True Then ' ADD THIS LINE BACK AND DELETE THE LINE BELOW ! 20180123
                                'If CurrentRatio > ROIThreshold(ROIIdx) And TriggerEnabled(ROIIdx) = True Then
                                AllTriggerStatus += TriggerTable(ROIIdx)
                                LastTrigRatio(ROIIdx) = CurrentRatio
                                TriggerEnabled(ROIIdx) = False
                                LastStimFrame(ROIIdx) = NumFrames
                                NumPostStim(ROIIdx) = 0
                            End If

                            '------------------ End Compare with thresh --------------------

                        Next ' next ROI

                    End If



                    ' ==============================  Sensory stimulation logic ===============================
                    If IsSensoryStim Then
                        For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                            NumPostStim(ROIIdx) += 1
                            CurrentRatio = ((SumROIintensity(ROIIdx) - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100
                            ' update rolling average, will use this for sensory stim 
                            CurrentAvgValue(ROIIdx) -= RatioBuffer(ROIIdx, AvgWindowCount)
                            RatioBuffer(ROIIdx, AvgWindowCount) = CurrentRatio / AvgWindowSize
                            CurrentAvgValue(ROIIdx) += CurrentRatio / AvgWindowSize
                            If AvgWindowCount = AvgWindowSize - 1 Then
                                PreviousAvgValue(ROIIdx) = CurrentAvgValue(ROIIdx)
                            End If
                            lblcurrentdf.Text = Format(CurrentRatio, "0.0")
                            Label_CurrentAvgValue.Text = Format(CurrentAvgValue(ROIIdx), "0.0")
                            averageLabel(ROIIdx).Text = Format(CurrentAvgValue(ROIIdx), "0.0")
                            If NumSenStim <= TotalSensoryStims Then
                                ' get sta
                                If NumFramesPostStim > NumStimFrames AndAlso NumFramesPostStim < NumStimFrames + StaNumAvgFrames AndAlso FlagSenStimFinished = False Then
                                    StaDffSum(ROIIdx) += CurrentRatio
                                    If ROIIdx = 0 Then
                                        NumStaFramesRecvd += 1
                                    End If

                                End If

                                If SenClamp Then
                                    ' clamp dff at threshold after response window for NumSenClamping frames
                                    If FireThisWhenBelowCheck(ROIIdx).Checked = True AndAlso NumFramesPostStim > NumFramesPostSenStimRequired + NumStimFrames AndAlso NumFramesPostStim < NumStimFrames + NumSenClampFrames AndAlso CurrentAvgValue(ROIIdx) < ROIThreshold(ROIIdx) Then
                                        AllTriggerStatus += TriggerTable(ROIIdx)
                                        AllTriggersEnabled = False
                                    End If
                                Else
                                    ' exclude roi from photostim targets if it has already acrossed threshold
                                    If FireThisWhenBelowCheck(ROIIdx).Checked = True AndAlso NumFramesPostStim <= NumFramesPostSenStimRequired AndAlso CurrentAvgValue(ROIIdx) > ROIThreshold(ROIIdx) AndAlso FlagROIAboveThresh(ROIIdx) = False Then ' record roi status in response window
                                        FlagROIAboveThresh(ROIIdx) = True
                                        SenStimAllTriggerStatus += TriggerTable(ROIIdx)
                                        AllTriggersEnabled = False
                                    End If
                                End If
                            Else
                                StatusText.Text = "End of sensory stimulus train"

                            End If

                        Next
                    End If


                    ' ==============================  Simple comparison logic (used for clamp) ===============================

                    If (Not IsSensoryStim) And (Not IsTrigTar) Then
                        For ROIIdx As Integer = 0 To NumTriggerROIs - 1

                            CurrentRatio = ((SumROIintensity(ROIIdx) - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100
                            ' update rolling average, will use this for sensory stim 
                            CurrentAvgValue(ROIIdx) -= RatioBuffer(ROIIdx, AvgWindowCount)
                            RatioBuffer(ROIIdx, AvgWindowCount) = CurrentRatio / AvgWindowSize
                            CurrentAvgValue(ROIIdx) += CurrentRatio / AvgWindowSize

                            If FireThisWhenAboveCheck(ROIIdx).Checked = True And CurrentAvgValue(ROIIdx) > ROIThreshold(ROIIdx) And TriggerEnabled(ROIIdx) = True Then
                                AllTriggerStatus += TriggerTable(ROIIdx)
                                LastTrigRatio(ROIIdx) = CurrentRatio
                                TriggerEnabled(ROIIdx) = False
                                LastStimFrame(ROIIdx) = NumFrames
                                NumPostStim(ROIIdx) = 0
                            End If

                            If FireThisWhenBelowCheck(ROIIdx).Checked = True And CurrentAvgValue(ROIIdx) < ROIThreshold(ROIIdx) Then
                                AllTriggerStatus += TriggerTable(ROIIdx)
                                NumActiveROIs += 1
                            End If
                            '------------------ End Compare with thresh --------------------
                        Next ' next ROI
                    End If
                    DisplayOffCounter += 1
                End If ' display = false andalso alltriggerenabled = true


                '  --- display on logic -- for debug use, as correct values
                If (IfUseCuda = False) And (DisplayOn = True Or (DisplayOffCounter = DisplayOffPeriod And ExperimentRunning = False)) Then
                    DisplayOnLogic(processedGrayPixels)
                End If

                '================================= TRIGGER  PHOTOSTIM ================================

                If NumFrames - LastTriFrame >= AllResetFrames Then ' take care of photostim artefects
                    AllTriggersEnabled = True
                    StatusText.Text = ""
                End If

                '----------------- send clamp on or off waveform to NI ao ------------------
                If (IsCalciumClamp) Then
                    RecordArray(2, NumFrames) = CStr(CurrentRatio)
                    If TrialStatus(NumFrames - 2) = False AndAlso TrialStatus(NumFrames - 1) = True Then
                        writerS1.BeginWriteMultiSample(True, ClampOnArray, Nothing, Nothing) 'write to channel a00
                    End If
                    If TrialStatus(NumFrames - 2) = True AndAlso TrialStatus(NumFrames - 1) = False Then
                        writerS1.BeginWriteMultiSample(True, ClampOffArray, Nothing, Nothing) 'write to channel a00
                    End If
                End If

                '---------------------------- sensory stim ----------------------------------
                If IsSensoryStim = True AndAlso NumSenStim <= TotalSensoryStims Then
                    If (Not SenClamp) AndAlso NumFramesPostStim = NumFramesPostSenStimRequired Then
                        AllTriggerStatus = SumTriggerTable - SenStimAllTriggerStatus 'drop rois that are above threshold
                        ReDim FlagROIAboveThresh(NumTriggerROIs - 1)
                        SenStimAllTriggerStatus = 0
                    End If

                End If

                '--------------------- control experiment - random stim ---------------------
                If IsControl = True AndAlso ExperimentRunning AndAlso AllTriggerStatus = 0 AndAlso AllTriggersEnabled = True AndAlso NumFrames - LastTriggerOnFrame >= WaitFramesAfterTrig Then
                    Randomize()
                    RandomNum = Rnd()
                    Dim this_rem = NumFrames Mod 10
                    LabelRnd.Text = Format(RandomNum, "0.00")
                    If (RandomNum > 0.961) AndAlso this_rem = 0 Then
                        DeliverTriggers(AllTriggerStatus)
                    End If
                End If

                If IsPlayBack AndAlso PlaybackCount < NumReadStimFrames Then
                    If NumFrames = ReadStimFrames(PlaybackCount) Then
                        AllTriggerStatus = ReadAllTriggerStatus(PlaybackCount)
                        PlaybackCount += 1
                    Else
                        AllTriggerStatus = 0
                    End If

                End If

                If ExperimentRunning AndAlso AllTriggerStatus > 0 Then
                    If IsControl = False Then
                        DeliverTriggers(AllTriggerStatus)
                    End If
                    LastTriggerOnFrame = NumFrames
                    AllTriggerStatus = 0
                    NumActiveROIs = 0
                End If

            End If ' end if numofbaseline > numrequired

        End If 'end if AllROIsSelected

        ' refresh live image window
        If DisplayOn = True Or (DisplayOffCounter = DisplayOffPeriod - 1 And ExperimentRunning = False) Then   ' uncomment this for experiment
            PictureBox1.Refresh()
        End If

        ' -- use cuda
        If IfUseCuda = True Then
            '

            If BaselineAquired = True AndAlso DisplayOn = True Then
                PictureBox1.Refresh()
                UpdateGraphs()
            End If
        End If

        If IfSaveBinary Then
            Dim debug_starttime As Long = m_oTimer.ElapsedMilliseconds
            Dim CustomerData(SamplesPerFrame * INT_SIZE - 1) As Byte
            System.Buffer.BlockCopy(processedGrayPixels2Save, 0, CustomerData, 0, processedGrayPixels2Save.Length * INT_SIZE - 1)
            Dim debug_endConversionTime As Long = m_oTimer.ElapsedMilliseconds
            fileStream.Write(CustomerData, 0, CustomerData.Length)
            Dim debug_endWriteTime As Long = m_oTimer.ElapsedMilliseconds
            NumByteWritten += CustomerData.Length
            Convert2BytesTime_label.Text = Format(debug_endConversionTime - debug_starttime, "0.00")
            WriteTime_label.Text = Format(debug_endWriteTime - debug_endConversionTime, "0.00")

        End If

        ' refresh labels
        ToHostTime_Label.Text = Format(toHostTime, "0.0")
        lblCurrentRatio.Text = Format(CurrentRatio, "0.0")
        shiftx_label.Text = CStr(shiftx_lb)
        shifty_label.Text = CStr(shifty_lb)
        ' measure timing
        Dim iElapsedMilliseconds As Long = m_oTimer.ElapsedMilliseconds
        TestPeriod = CStr(iElapsedMilliseconds - teststart)   ' processing time
        ThisFramePeriod = iElapsedMilliseconds - iLastElapsedMilliseconds
        iLastElapsedMilliseconds = iElapsedMilliseconds
        m_iProcessingIterations += 1
        lblPollingTime.Text = Format(m_fPollingTime2 / m_iPollingIterations2, "0.000")
        lblPollingPeriod.Text = Format(m_fPollingTime / m_iPollingIterations, "0.000")
        lblFrameTime.Text = Format(ThisFramePeriod, "0.000")
        lblTestTime.Text = Format(TestPeriod, "0.000") 'processing time

        If Not IsTrigTar Then
            For i As Integer = 0 To NumTriggerROIs - 1
                averageValue(i).Text = Format(SumROIintensity(i), "0.0")
            Next
        End If

        ' take a note of timing 20180123
        If NumFrames <= 1000 Then
            TestPeriodArray(NumFrames - 1) = TestPeriod

        End If

    End Sub


    Private Sub DisplayOnLogic(ByRef processedGrayPixels As Integer()) 'display traces in sliding windows
        'If DisplayOn = True Or ExperimentRunning = False Then  ' test only
        AllTriggerStatus = 0
        DisplayOffCounter = 0
        NumActiveROIs = 0
        'main loop through selected ROIs, data points
        For ROIIdx As Integer = 0 To NumTriggerROIs - 1
            For DataIdx As Integer = 0 To SlidingWindowSize - 1
                If DataIdx = SlidingWindowSize - 1 Then 'This wil be the most recent data to display

                    If DisplayOn = True Then
                        SumPixelValue = 0

                        NumPixelsForROI = ROIMaskIndices(ROIIdx).Count
                        For PixelIdx As Integer = 0 To NumPixelsForROI
                            SumPixelValue += processedGrayPixels(ROIMaskIndices(ROIIdx)(PixelIdx))
                        Next

                    Else
                        SumPixelValue = SumROIintensity(ROIIdx)
                    End If

                    CurrentValue = ((SumPixelValue - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100

                    SlidingWindowArray(ROIIdx, DataIdx) = CurrentValue

                    'The main logic (if ROI passes threshold)

                    If ExperimentRunning And AllTriggersEnabled = True Then


                        If (StartRecording = False) OrElse (StartRecording = True AndAlso TrialStatus(NumFrames - 1) = True) Then

                            If FireThisWhenAboveCheck(ROIIdx).Checked = True Then
                                ' If FireWhenAbove Then
                                If CurrentValue > ROIThreshold(ROIIdx) And _
                                   TriggerEnabled(ROIIdx) = True Then
                                    AllTriggerStatus += TriggerTable(ROIIdx)
                                End If
                            End If

                            If FireThisWhenBelowCheck(ROIIdx).Checked = True Then
                                'If FireWhenBelow Then
                                If CurrentValue < ROIThreshold(ROIIdx) And _
                                TriggerEnabled(ROIIdx) = True Then
                                    AllTriggerStatus += TriggerTable(ROIIdx)
                                End If

                            End If
                        End If

                    End If ' end experiment running

                Else  'Existing data is shifted backwards in buffer

                    SlidingWindowArray(ROIIdx, DataIdx) = SlidingWindowArray(ROIIdx, DataIdx + 1)
                End If
                TempArrayForPlot(DataIdx) = SlidingWindowArray(ROIIdx, DataIdx)  'temp array to be loaded into graph
                RoundTempArray(DataIdx) = Round(TempArrayForPlot(DataIdx), 2)
            Next

            ' update graph data
            If ROIIdx < 4 Then
                Chart1.Series(ROIIdx).Points.DataBindY(RoundTempArray)

            ElseIf ROIIdx < 8 Then
                Chart2.Series(ROIIdx - 4).Points.DataBindY(RoundTempArray)

            ElseIf ROIIdx < 12 Then
                Chart3.Series(ROIIdx - 8).Points.DataBindY(RoundTempArray)

            End If

            ' update thresholds
            If LockThresholds = False Then
                av = TempArrayForPlot.Average()
                sd = getStandardDeviation(TempArrayForPlot)

                averageLabel(ROIIdx).Text = CStr(av)
                threshSpinbox(ROIIdx).Value = CDec(av + (4 * sd))  ' threshold is X*SD
            End If
        Next
        ' refresh live graphs
        Chart1.Refresh()
        Chart2.Refresh()
        Chart3.Refresh()

    End Sub
    Private Sub UpdateGraphs() 'display traces in sliding windows

        AllTriggerStatus = 0
        DisplayOffCounter = 0
        NumActiveROIs = 0
        'main loop through selected ROIs, data points
        For ROIIdx As Integer = 0 To NumTriggerROIs - 1
            For DataIdx As Integer = 0 To SlidingWindowSize - 1
                If DataIdx = SlidingWindowSize - 1 Then 'This wil be the most recent data to display

                    CurrentValue = SumROIintensity(ROIIdx) / ROIMaskIndices(ROIIdx).Count
                    'CurrentValue = ((SumROIintensity(ROIIdx) - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100
                    SlidingWindowArray(ROIIdx, DataIdx) = CurrentValue
                Else  'Existing data is shifted backwards in buffer
                    SlidingWindowArray(ROIIdx, DataIdx) = SlidingWindowArray(ROIIdx, DataIdx + 1)
                End If
                TempArrayForPlot(DataIdx) = SlidingWindowArray(ROIIdx, DataIdx)  'temp array to be loaded into graph
                RoundTempArray(DataIdx) = Round(TempArrayForPlot(DataIdx), 2)
            Next

            ' update graph data
            If ROIIdx < 4 Then
                Chart1.Series(ROIIdx).Points.DataBindY(RoundTempArray)

            ElseIf ROIIdx < 8 Then
                Chart2.Series(ROIIdx - 4).Points.DataBindY(RoundTempArray)

            ElseIf ROIIdx < 12 Then
                Chart3.Series(ROIIdx - 8).Points.DataBindY(RoundTempArray)

            End If

        Next
        ' refresh live graphs
        Chart1.Refresh()
        Chart2.Refresh()
        Chart3.Refresh()
 

    End Sub
    Private Sub ProcessGray16(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByVal ROImasks() As Integer, ByRef processedGrayPixels() As Integer)
        ' only process pixels within roi masks, this is fast
        ReDim SumROIintensity(NumTriggerROIs - 1)
        For i As Integer = 0 To (samples \ m_iSamplesPerPixel) - 1
            Dim ind As Integer = ROImasks(i)
            If ind > 0 Then
                Dim iIntensity As Integer
                Dim iCount As Integer = 0
                Dim iSum As Long = 0
                For iSample As Integer = 0 To m_iSamplesPerPixel - 1
                    iIntensity = grayPixels(offset + i * m_iSamplesPerPixel + iSample) - 8192
                    If iIntensity >= 0 Then
                        iSum += iIntensity
                        iCount += 1
                    End If
                Next
                If iSum > 0 Then
                    processedGrayPixels(i) = CInt(iSum / iCount)
                    SumROIintensity(ind - 1) += processedGrayPixels(i)
                End If
            End If

        Next
    End Sub           ' test Function; change to sub for real experiment.

    Private Sub ProcessGray16forSave(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByVal ROImasks() As Integer, ByRef processedGrayPixels() As UInt16)  ' delete as integer ()
        ' process all received pixels for saving to binary file; DOES NOT DEAL WITH FLIPPING ROWS
        ReDim SumROIintensity(NumTriggerROIs - 1)
        For i As Integer = 0 To (samples \ m_iSamplesPerPixel) - 1
            Dim ind As Integer = ROImasks(i)
            Dim iIntensity As Integer
            Dim iCount As Integer = 0
            Dim iSum As Long = 0
            For iSample As Integer = 0 To m_iSamplesPerPixel - 1
                iIntensity = grayPixels(offset + i * m_iSamplesPerPixel + iSample) - 8192
                If iIntensity >= 0 Then
                    iSum += iIntensity
                    iCount += 1
                End If
            Next
            If iSum > 0 Then
                processedGrayPixels(i) = CInt(iSum / iCount)
                If ind > 0 Then
                    SumROIintensity(ind - 1) += processedGrayPixels(i)
                End If
            End If

        Next
    End Sub           ' test Function; change to sub for real experiment.

    Private Function ConvertAvgGrayToRGB(ByRef AbgGrayPixels() As Integer, ByVal Count As Integer) As Integer()
        Dim rgbPixels(AbgGrayPixels.Length - 1) As Integer
        For i As Integer = 0 To AbgGrayPixels.Length - 1
            Dim forRGB As Integer = (AbgGrayPixels(i) / Count) >> m_iBitsToShift
            rgbPixels(i) = forRGB Or (forRGB << 8) Or (forRGB << 16)
        Next
        Return rgbPixels
    End Function

    Private Function ConvertGray16ToRGB(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByRef processedGrayPixels() As Integer) As Integer()
        ' process all received pixels and get rgb image for display
        Dim rgbPixels((samples \ m_iSamplesPerPixel) - 1) As Integer
        For i As Integer = 0 To (samples \ m_iSamplesPerPixel) - 1
            Dim iIntensity As Integer
            Dim iCount As Integer = 0
            Dim iSum As Long = 0
            For iSample As Integer = 0 To m_iSamplesPerPixel - 1
                ' subtract 8192
                iIntensity = grayPixels(offset + i * m_iSamplesPerPixel + iSample) - 8192

                'only count postive (or zero) values
                If iIntensity >= 0 Then
                    iSum += iIntensity
                    iCount += 1
                End If
            Next
            'iIntensity = CInt(iSum \ iCount) >> m_iBitsToShift

            'average the pixel samples together
            If iSum > 0 Then
                processedGrayPixels(i) = CInt(iSum / iCount)
            End If


            'get 8bit values for rgb bmp image (shift bits)
            Dim forRGB As Integer = processedGrayPixels(i) >> m_iBitsToShift
            ' put the values into r,g and b channels
            'rgbPixels(i) = CInt(forRGB << 16) Or CInt(forRGB << 8) Or CInt(forRGB)
            rgbPixels(i) = forRGB Or (forRGB << 8) Or (forRGB << 16)
        Next


        ' Save frame array to text file
        'Dim strArray() As String = Array.ConvertAll(Of Integer, String)(processedGrayPixels, Function(x) x.ToString())
        'Dim FileName As String = "output_" & DateTime.Now.ToString("yyyyMMdd-HHmmss") & "_" & m_iProcessingIterations & ".txt"
        'Dim fstr As String = ""
        'Dim sw As System.IO.StreamWriter = New System.IO.StreamWriter(FileName)
        'For i As Int32 = strArray.GetLowerBound(0) To strArray.GetUpperBound(0)
        '    fstr += strArray(i) + ", "
        '    sw.Write(fstr)
        '    fstr = ""
        'Next
        'fstr = ""
        'sw.WriteLine(fstr)
        'sw.Flush()
        'sw.Close()

        Return rgbPixels
    End Function

    Private Sub ProcessGrayPixelsFullFrame(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByRef processedGrayPixels() As Integer)
        ' process all received pixels and get rgb image for display

        For i As Integer = 0 To (samples \ m_iSamplesPerPixel) - 1
            Dim iIntensity As Integer
            Dim iCount As Integer = 0
            Dim iSum As Long = 0
            For iSample As Integer = 0 To m_iSamplesPerPixel - 1
                ' subtract 8192
                iIntensity = grayPixels(offset + i * m_iSamplesPerPixel + iSample) - 8192

                'only count postive (or zero) values
                If iIntensity >= 0 Then
                    iSum += iIntensity
                    iCount += 1
                End If
            Next

            'average the pixel samples together
            If iSum > 0 Then
                processedGrayPixels(i) = CInt(iSum / iCount)
            End If
        Next

    End Sub

    Private Sub numBitsToShift_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles numBitsToShift.ValueChanged
        m_iBitsToShift = CInt(numBitsToShift.Value)
    End Sub


    Private Sub numSamplesPerPixel_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles numSamplesPerPixel.ValueChanged
        m_iSamplesPerPixel = CInt(numSamplesPerPixel.Value)
    End Sub

    Private Sub chkFlipOdd_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkFlipOdd.CheckedChanged
        m_flipOdd = chkFlipOdd.Checked
    End Sub

    Private Sub chkFlipEven_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles chkFlipEven.CheckedChanged
        m_flipEven = chkFlipEven.Checked
    End Sub

    Private Sub ThresholdValueChanged(ByVal sender As Object, ByVal e As System.EventArgs)
        Dim control As NumericUpDown = CType(sender, NumericUpDown)
        Dim ROIIdx As Integer = CInt(control.Tag)
        ROIThreshold(ROIIdx) = control.Value
        Dim ThresholdLineData(SlidingWindowSize) As Single
        For i As Integer = 0 To SlidingWindowSize - 1
            ThresholdLineData(i) = ROIThreshold(ROIIdx)
        Next
        If ROIIdx < 4 Then
            Chart1.Series(ROIIdx + 4).Points.DataBindY(ThresholdLineData)  ' +4 because 4 graphs are hardcoded...
        ElseIf ROIIdx < 8 Then
            Chart2.Series(ROIIdx).Points.DataBindY(ThresholdLineData)  ' +4 because 4 graphs are hardcoded...
        ElseIf ROIIdx < 12 Then
            Chart3.Series(ROIIdx - 4).Points.DataBindY(ThresholdLineData)  ' +4 because 4 graphs are hardcoded...

        End If

    End Sub


    Private Sub DeliverTriggers(ByVal ROIIdx As Integer)
        If AllResetFrames > 0 Then
            AllTriggersEnabled = False
        End If

        '' ---------     Trigger by number of pulses   ------------------------
        'Dim AdvanceSLMBy As Integer = ROIIdx + 1 - CurrentSLMPattern
        'If AdvanceSLMBy < 0 Then
        '    AdvanceSLMBy = NumSLMpatterns + AdvanceSLMBy
        'End If
        'For i As Integer = 0 To OutputArrayLength
        '    OutputArray(0, i) = TriggersToSLM(AdvanceSLMBy, i)
        '    OutputArray(1, i) = TriggerToPV(AdvanceSLMBy, i)
        'Next
        'writer.BeginWriteMultiSample(True, OutputArray, Nothing, Nothing)
        '' --------------------------------------------------------------------




        ''----------    Trigger by TCP to PV - SLOW   -------------------------
        'm_pl.SendScriptCommands("-mp")
        'm_pl.SendScriptCommands("-mp .5 .5 10 Fidelity 250 true .03 3 ")
        'm_pl_s.SendScriptCommands("-mp .7 .7 30 Fidelity 250 true .015 3 ")
        '----------------------------------------------------------------------




        '' -----------     Trigger by analog output   -------------------------
        'For i As Integer = 0 To 10
        '    AnalogOutputArray(0, i) = AnalogLUT(AllTriggerStatus)
        'Next

        'writer.BeginWriteMultiSample(True, AnalogOutputArray, Nothing, Nothing)
        ''writerD.BeginWriteMultiSamplePort(True, DigitalOutputArray, Nothing, Nothing)
        ''---------------------------------------------------------------------




        ' ''----------    Send pattern index to Zoo, then trigger stim   --------------------------
        If NumTriggerROIs > 1 AndAlso TCPConnected Then
            If TrialStatus(NumFrames - 1) = True Or IsSensoryStim = True Then
                If (AllTriggerStatus > 0) Then
                    Dim vTimeout As Integer = 50  'ms 
                    Dim sendbytes() As Byte = System.Text.Encoding.ASCII.GetBytes(AllTriggerStatus.ToString(SLMPatternFormat))
                    'writer.BeginWriteMultiSample(True, ToSLMOutputArray, Nothing, Nothing)  ' for test only
                    TCPClients.Client.Send(sendbytes)
                    TCPClients.ReceiveTimeout = vTimeout
                    sendbytes = New [Byte](3) {}
                    Dim bytes As Int32 = TCPClientStream.Read(sendbytes, 0, sendbytes.Length)
                    Dim echoData = System.Text.Encoding.ASCII.GetString(sendbytes, 0, bytes)

                    writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01

                    StatusText.Text = "Pattern " & echoData & " stimulated"
                    StimNum += 1

                    '' take notes
                    lblStimNum.Text = CStr(StimNum)
                    RecordArray(0, StimNum) = CStr(AllTriggerStatus)
                    RecordArray(1, StimNum) = CStr(NumFrames)
                    RecordArray(2, StimNum) = echoData
                End If
            Else
                StatusText.Text = "Pattern " & CStr(AllTriggerStatus) & " triggered"
                ' LastTriThresh(ROIIdx) = ROIThreshold(ROIIdx)

            End If
        Else
            '-------------  Simple trigger   only send spirals during experiment --------------------------

            If StartRecording = True Then
                If TrialStatus(NumFrames - 1) = True Or IsSensoryStim = True Then
                    writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01
                    StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " stimulated"
                    StimNum += 1
                Else
                    StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " on"
                End If

            Else
                writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01
                StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " stimulated"
                ' LastTriThresh(ROIIdx) = ROIThreshold(ROIIdx)
            End If
            ' take notes
            lblStimNum.Text = CStr(StimNum)
            RecordArray(0, StimNum) = CStr(AllTriggerStatus)
            RecordArray(1, StimNum) = CStr(NumFrames)

            '------------------------     end simple trigger --------------------------
        End If

        '-------------------End  Trigger by TCP to Zoo  --------------------




        '-------------  Simple trigger   only send spirals during experiment --------------------------

        'If StartRecording = True Then
        '    If TrialStatus(NumFrames - 1) = True Then
        '        writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01
        '        StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " stimulated"
        '        StimNum += 1
        '    Else
        '        StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " on"
        '    End If

        'Else
        '    writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01
        '    StatusText.Text = "Pattern " & Convert.ToString(AllTriggerStatus) & " stimulated"
        '    ' LastTriThresh(ROIIdx) = ROIThreshold(ROIIdx)
        'End If
        '' take notes
        'lblStimNum.Text = CStr(StimNum)
        'RecordArray(0, StimNum) = CStr(AllTriggerStatus)
        'RecordArray(1, StimNum) = CStr(NumFrames)

        '------------------------     end simple trigger --------------------------




        'For i As Integer = 5 To (NumTriggerROIs + 4)
        '    RecordArray(i, StimNum) = ROIThreshold(i - 5)
        'Next
        'RecordArray(1, StimNum) = CStr(LastTriggerTime(ROIIdx))


        'Dim S As String = ""        ' Record threshold of all ROIs
        'For Each item As String In ROIThreshold
        '    S &= item & " "
        'Next

        'Dim s As String = String.Join(";", ROIThreshold.ToArray())
        'RecordArray(2, StimNum) = CStr(S)


        ' Update labels
        SLMPatternLabel.Text = CStr(AllTriggerStatus)
        lblTriggerInterval.Text = CStr(m_oTimer.ElapsedMilliseconds - LastTrigger)
        LastTrigger = m_oTimer.ElapsedMilliseconds
        LastTriFrame = NumFrames


        ' Redim parameters
        AllTriggerStatus = 0    ' zero pattern No.
        NumActiveROIs = 0


    End Sub

    Private Function getStandardDeviation(ByVal array As Double()) As Double
        Dim mean As Double = array.Average()
        Dim squares As Double = 0
        Dim squareAvg As Double

        For Each value As Double In array
            squares += Pow(value - mean, 2)
        Next
        squareAvg = squares / (array.Length - 1)

        Return Math.Sqrt(squareAvg)
    End Function

    Private Sub LockThresholdsButton_clicked(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button2.Click
        LockThresholds = Not LockThresholds
        If LockThresholds Then
            Button2.Text = "Unlock thresholds"
        Else
            Button2.Text = "Lock thresholds"
        End If
    End Sub

    Private Sub EnableTriggersButton_clicked(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button4.Click
        ExperimentRunning = Not ExperimentRunning
        If ExperimentRunning Then
            Button4.Text = "Pause experiment"
        Else

            Button4.Text = "Resume experiment"

        End If
    End Sub


    Private Sub ButtonNewRecording_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonNewRecording.Click
        UpdateAllForNewRecording()
    End Sub

    Private Sub UpdateAllForNewRecording()
        writerS1.BeginWriteMultiSample(True, ClampOffArray, Nothing, Nothing) 'write to channel a00
        writerS2.BeginWriteMultiSample(True, ToPVreset, Nothing, Nothing) 'write to channel a01
        NumExperiment = CInt(TboxAqNum.Text) + 1
        TboxAqNum.Text = CStr(NumExperiment)
        ReDim RecordArray(2, 12000)
        ReDim TestPeriodArray(1000)
        StimNum = 0
        NumFrames = 0
        SamplesReceived = 0
        BaselineAquired = False
        NumBaselineSamples = 0
        LastTriFrame = 0

        ReDim SumBaseline(NumTriggerROIs)

        AllTriggersEnabled = False

        ' redim trigger-targets
        ReDim SlidingAvg(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim SlidingStd(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim ROIav(NumTriggerROIs - 1)
        ReDim ROIstd(NumTriggerROIs - 1)
        ReDim LastStimFrame(NumTriggerROIs - 1)
        ReDim tempidx(NumTriggerROIs - 1)
        ReDim NoiseStd(NumTriggerROIs - 1)
        ReDim NumNoiseStdFrames(NumTriggerROIs - 1)


        'for sensory stim
        SenStimDetected = False
        NumFramesPostStim = 0
        NumSenStim = 0
        NumActiveROIs = 0
        AllTriggerStatus = 0
        ReDim FlagROIAboveThresh(NumTriggerROIs - 1)
        SenStimAllTriggerStatus = 0
        ReDim StaDffSum(NumTriggerROIs - 1)
        FlagSenStimFinished = False
        NumStaFramesRecvd = 0

        'for low-pass=filter
        ReDim NumPostStim(NumTriggerROIs - 1)
        ReDim LastTriThresh(NumTriggerROIs - 1)


        'other parameters
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerEnabled(i) = True
        Next
        ReDim CurrentAvgValue(NumTriggerROIs - 1)
        ReDim PreviousAvgValue(NumTriggerROIs - 1)
        ReDim RatioBuffer(NumTriggerROIs - 1, AvgWindowSize - 1)
        AvgWindowCount = 0
        PlaybackCount = 0
        'flush 
        Dim iSamples As Integer = 1
        While iSamples > 0
            Dim iBuffer() As Int16 = m_pl.ReadRawDataStream(iSamples)
        End While
    End Sub

    Private Sub Label1_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Label1.Click

    End Sub

    Private Sub IsMaster_checkbox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles IsMaster_checkbox.CheckedChanged

        If IsMaster_checkbox.Checked Then
            IsSlave_checkbox.CheckState = CheckState.Unchecked
            IsMaster = True
            IsSlave = False
        End If

    End Sub

    Private Sub IsSlave_checkbox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles IsSlave_checkbox.CheckedChanged

        If IsSlave_checkbox.Checked Then
            IsMaster_checkbox.CheckState = CheckState.Unchecked
            IsMaster = False
            IsSlave = True
        End If

    End Sub



    Private Sub updateBaseline_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles updateBaseline.Click
        NumBaselineSamples = 0
        BaselineAquired = False
        ReDim SumBaseline(NumTriggerROIs)
    End Sub







    Private Sub ButtonZoom2_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonZoom2.Click
        Dim mag As Single = 2 / RefMagnification
        zoomChange(mag)
    End Sub

    Private Sub ButtonZoomRef_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonZoomRef.Click
        Dim mag As Single = 1.14 / RefMagnification
        zoomChange(mag)
    End Sub

    Private Sub ButtonZoom4_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonZoom4.Click
        Dim mag As Single = 4 / RefMagnification
        zoomChange(mag)
    End Sub

    Private Sub StartRecordingButton_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles StartRecordingButton.Click

        StartRecording = Not StartRecording
        NumBaselineSamples = 0   'get new baseline
        If StartRecording Then
            StartRecordingButton.Text = "Stop recording"
            StartRecordingButton.BackColor = Color.Green
            NumBaselineSpRequired = 50

            ' initialise binary file writer
            If IfSaveBinary Then
                InitialiseBinStream()
            End If
        Else

            StartRecordingButton.Text = "Start recording"
            StartRecordingButton.BackColor = Color.Gray
            NumBaselineSpRequired = 150

            If IfSaveBinary Then
                fileStream.Close()
            End If
        End If
    End Sub


    Private Sub InitialiseBinStream()
        BinFileName = TextBoxFilepath.Text & "\" & TboxFilename.Text & ".bin"
        If IO.File.Exists(path:=BinFileName) Then
            MessageBox.Show("Binary file already exist", "My Application", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk)
            Return
        Else
            NumByteWritten = 0
            'create a fileStream instance to pass to BinaryWriter object 
            fileStream = New IO.FileStream(path:=BinFileName, mode:=IO.FileMode.CreateNew, access:=IO.FileAccess.Write)
            'create binary writer instance 
            ' bWrite = New BinaryWriter(output:=fileStream)
        End If
    End Sub


    Private Sub DisplayOnCheckbox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles DisplayOnCheckbox.CheckedChanged
        DisplayOn = Not DisplayOn
        ReDim SlidingAvg(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim SlidingStd(NumTriggerROIs - 1, SlidingWindowSize - 1)

    End Sub

    Private Sub UpdatePollBtn_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles UpdatePollBtn.Click
        m_iTotalSamples = 0
        m_fPollingTime = 0
        m_iPollingIterations = 0
        m_fProcessingTime = 0
        m_iProcessingIterations = 0
    End Sub


    Private Sub btnSenStim_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles btnSenStim.Click
        IsSensoryStim = Not IsSensoryStim

        If IsSensoryStim Then
            btnSenStim.BackColor = Color.Green
        Else
            btnSenStim.BackColor = Color.Gray
        End If
    End Sub

    Private Sub numWaitingFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles numWaitingFrames.ValueChanged
        NumFramesPostSenStimRequired = CInt(numWaitingFrames.Value)
    End Sub

    Private Sub BtnConnect_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles BtnConnect.Click
        Try
            TCPClients = New Sockets.TcpClient("128.40.156.163", 8888)
            TCPClientStream = TCPClients.GetStream()
            TCPConnected = True
        Catch ex As Exception
            MsgBox("Cannot connect to server")
        End Try


    End Sub


    Private Sub numAllResetFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles numAllResetFrames.ValueChanged
        AllResetFrames = CInt(numAllResetFrames.Value)
    End Sub

    Private Sub Button5_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button5.Click
        ' set a cell as trigger cell, stim the below-thresh targets when trigger cell goes above-thresh
        TrgCellIdx = CInt(TriggerCellIdx.Value - 1)
        IsTrigTar = Not IsTrigTar

        If IsTrigTar Then
            Button5.BackColor = Color.Green
            NumBaselineSamples = NumBaselineSpRequired
        Else
            Button5.BackColor = Color.Gray
            NumBaselineSamples = 0
        End If
        ReDim TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1)
        For i As Integer = NumControlFrames To TotalNumFrames - 1
            TrialStatus(i) = True
        Next



    End Sub

    Private Sub TriggerCellIdx_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles TriggerCellIdx.ValueChanged
        TrgCellIdx = CInt(TriggerCellIdx.Value) - 1

    End Sub


    Private Sub NumericUpDownWaitAfterTrig_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownWaitAfterTrig.ValueChanged
        If (IsTrigTar) Then
            WaitFramesAfterTrig = (NumericUpDownWaitAfterTrig.Value) - 1
        End If
    End Sub

    Private Sub NumericUpDownN_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownN.ValueChanged
        N = CInt(NumericUpDownN.Value)
    End Sub



    Private Sub NumericUpDown_controlframes_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDown_controlframes.ValueChanged
        NumControlFrames = CInt(NumericUpDown_controlframes.Value)
        ''-- for trigger-target
        ReDim TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1)
        For i As Integer = NumControlFrames To TotalNumFrames - 1
            TrialStatus(i) = True
        Next
    End Sub

    Private Sub VScrollBar1_Scroll(ByVal sender As System.Object, ByVal e As System.Windows.Forms.ScrollEventArgs)

    End Sub

    Private Sub NumericUpDownNumbaseline_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumbaseline.ValueChanged
        NumBaselineSpRequired = CInt(NumericUpDownNumbaseline.Value)
    End Sub

    Private Sub ButtonCalciumClamp_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonCalciumClamp.Click
        IsCalciumClamp = Not IsCalciumClamp
        If IsCalciumClamp Then
            ButtonCalciumClamp.BackColor = Color.Green
            ReDim TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1)
            For j As Integer = 0 To NumClamps - 1
                For i As Integer = NumClampIntervalFrames + (NumClampIntervalFrames + NumClampOnFrames) * j To (NumClampIntervalFrames + NumClampOnFrames) * (j + 1)
                    TrialStatus(i) = True
                Next
            Next
        Else
            ButtonCalciumClamp.BackColor = Color.Gray
        End If
    End Sub

    Private Sub NumericUpDownNumkClampOn_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumkClampOn.ValueChanged
        NumClampOnFrames = CInt(NumericUpDownNumkClampOn.Value)
    End Sub

    Private Sub NumericUpDownNumClamps_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumClamps.ValueChanged
        NumClamps = CInt(NumericUpDownNumClamps.Value)
    End Sub


    Private Sub NumericUpDownNumInterClamps_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumInterClamps.ValueChanged
        NumClampIntervalFrames = CInt(NumericUpDownNumInterClamps.Value)
    End Sub

    Private Sub ButtonUpdateClampSettings_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonUpdateClampSettings.Click
        If (IsCalciumClamp) Then
            ReDim TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1)
            For j As Integer = 0 To NumClamps - 1
                For i As Integer = NumClampIntervalFrames + (NumClampIntervalFrames + NumClampOnFrames) * j To (NumClampIntervalFrames + NumClampOnFrames) * (j + 1)
                    TrialStatus(i) = True
                Next
            Next
        End If
    End Sub


    Private Sub ButtonTestTrigger_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonTestTrigger.Click
        writerS1.BeginWriteMultiSample(True, ToSensoryStim, Nothing, Nothing) 'write to channel a00
        TCPClients2.Client.Send(sendbytes2)

    End Sub


    Private Sub NumericUpDownNumStimFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumStimFrames.ValueChanged
        NumStimFrames = CInt(NumericUpDownNumStimFrames.Value)
    End Sub

    Private Sub NumericUpDownNumStims_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumStims.ValueChanged
        TotalSensoryStims = CInt(NumericUpDownNumStims.Value)
    End Sub

    Private Sub BtnSensoryConnect_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles BtnSensoryConnect.Click
        Try
            ' TCPClients2 = New Sockets.TcpClient("128.40.156.163", 8070) 'ip address for zoo
            TCPClients2 = New Sockets.TcpClient("128.40.156.162", 8070) 'ip address for optiplex
            TCPClientStream2 = TCPClients2.GetStream()
            serverConnected = True
        Catch ex As Exception
            serverConnected = False
            MsgBox("Cannot connect to server")
        End Try

    End Sub

    Private Sub NumericUpDownStimType_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownStimType.ValueChanged
        SensoryStimType = NumericUpDownStimType.Value
        sendbytes2 = System.Text.Encoding.ASCII.GetBytes(SensoryStimType)
    End Sub


    Private Sub NumUpDownNumCLframes_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumUpDownNumCLframes.ValueChanged
        TotalNumFrames = CInt(NumUpDownNumCLframes.Value) + NumControlFrames
        ReDim TrialStatus(TotalNumFrames + NumControlFrames + 100 - 1)
        For i As Integer = NumControlFrames To TotalNumFrames - 1
            TrialStatus(i) = True
        Next

    End Sub

    Private Sub NumericUpDown_AvgWindowSize_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDown_AvgWindowSize.ValueChanged
        AvgWindowSize = CInt(NumericUpDown_AvgWindowSize.Value)
        ReDim RatioBuffer(NumTriggerROIs - 1, AvgWindowSize - 1)
        AvgWindowCount = 0
    End Sub

    Private Sub CheckBoxControl_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles CheckBoxControl.CheckedChanged
        IsControl = Not IsControl
    End Sub


    Private Sub Senclamp_CheckBox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Senclamp_CheckBox.CheckedChanged
        SenClamp = Not SenClamp
    End Sub

    Private Sub NumericUpDown1_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDown1.ValueChanged
        NumSenClampFrames = CInt(NumericUpDown1.Value)
    End Sub

    Private Sub showSta_pushbutton_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles showSta_pushbutton.Click
        For i As Integer = 0 To NumTriggerROIs - 1
            StaDff(i) = StaDffSum(i) / NumStaFramesRecvd
            STA_DataGridView.Rows.Add("ROI" & CStr(i + 1), CStr(StaDff(i)))
        Next
    End Sub


    Private Sub SendTTLtrain_CheckBox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles SendTTLtrain_CheckBox.CheckedChanged
        FlagSendTTLtrain = Not FlagSendTTLtrain
    End Sub



    Private Sub NumFrameToAvg_NumericUpDown_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumFrameToAvg_NumericUpDown.ValueChanged
        StaNumAvgFrames = NumFrameToAvg_NumericUpDown.Value
    End Sub

    Private Sub SaveBinary_CheckBox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles SaveBinary_CheckBox.CheckedChanged
        IfSaveBinary = Not IfSaveBinary
    End Sub

    Private Sub GetNameFromPVButton_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles GetNameFromPVButton.Click
        Dim PathName As String = m_pl.GetState("directory", 1)
        Dim tSeriesName As String = m_pl.GetState("directory", 4)
        Dim tSeriesIter As Integer = m_pl.GetState("fileIteration", 4)

        BinFileName = PathName & "\" & tSeriesName & "-" & tSeriesIter & ".bin"
        TextBoxFilepath.Text = PathName
        TboxFilename.Text = tSeriesName & "-" & tSeriesIter

    End Sub


    Private Sub UpdateROIsButton_Click(sender As Object, e As EventArgs) Handles UpdateROIsButton.Click
        NumTriggerROIs = NumROIs_NumericUpDown.Value

        SelectedROIsCount = 0  'used for start, count numebr of rois as they are seleted
        flipdone = False
        ' redim roi-related parameters
        ROIMasks = New List(Of Image)
        ReDim ROIMaskIndices(NumTriggerROIs)
        ReDim AllMaskROIArray(512 * 512 - 1)
        ReDim SumROIintensity(NumTriggerROIs - 1)

        ReDim ROIMaskIndicesX(NumTriggerROIs - 1)
        ReDim ROIMaskIndicesY(NumTriggerROIs - 1)
        ReDim ROICoordsX(NumTriggerROIs - 1)
        ReDim ROICoordsY(NumTriggerROIs - 1)
        ReDim ROICoords(NumTriggerROIs - 1)



        ReDim SlidingWindowArray(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim ROIThreshold(NumTriggerROIs - 1)


        ReDim LastRatio(NumTriggerROIs - 1)
        ReDim CurrentAvgValue(NumTriggerROIs - 1) ' average every AvgWindowSize frames
        ReDim PreviousAvgValue(NumTriggerROIs - 1)
        ReDim RatioBuffer(NumTriggerROIs - 1, AvgWindowSize - 1)  ' for rolling average

        ReDim ROIBaseline(NumTriggerROIs - 1)
        ReDim SumBaseline(NumTriggerROIs - 1)

        ReDim TriggerTable(NumTriggerROIs - 1)
        SumTriggerTable = 0
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerTable(i) = Math.Pow(2, i)
            SumTriggerTable += TriggerTable(i)
        Next
        OutputArrayLength = 15 * NumTriggerROIs - 1

        ReDim TriggersToSLM(NumTriggerROIs, OutputArrayLength)  'should be size of NumTriggerROIs, 8 works for now
        ReDim TriggerToPV(NumTriggerROIs, OutputArrayLength)   'should be size of NumTriggerROIs, 8 works for now
        ReDim TriggerEnabled(NumTriggerROIs - 1)
        ReDim LastStimFrame(NumTriggerROIs - 1)

        ReDim FlagROIAboveThresh(NumTriggerROIs - 1)
        ReDim StaDffSum(NumTriggerROIs - 1)
        ReDim StaDff(NumTriggerROIs - 1)


        ReDim SlidingAvg(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim SlidingStd(NumTriggerROIs - 1, SlidingWindowSize - 1)
        ReDim ROIav(NumTriggerROIs - 1)
        ReDim ROIstd(NumTriggerROIs - 1)
        ReDim LastTrigRatio(NumTriggerROIs - 1)
        ReDim tempidx(NumTriggerROIs - 1)


        ReDim NoiseStd(NumTriggerROIs - 1)
        ReDim NumNoiseStdFrames(NumTriggerROIs - 1)
        ReDim LastTriThresh(NumTriggerROIs - 1)
        ReDim NumPostStim(NumTriggerROIs - 1)

        StatusText.Text = "Select " & Convert.ToString(NumTriggerROIs - SelectedROIsCount) & " trigger ROI(s)"
        Dim bmp As New Drawing.Bitmap(512, 512)
        PictureBox2.Image = bmp

        ' initialise ROI masks
        For i As Integer = 0 To NumTriggerROIs - 1
            'Initialise ROI mask indices array(queue because unknown size)
            ROIMaskIndices(i) = New Queue(Of Integer)()
            ROIMaskIndicesX(i) = New Queue(Of Integer)()
            ROIMaskIndicesY(i) = New Queue(Of Integer)()
        Next


        UpdateAllForNewRecording()

    End Sub

    Private Sub SendCentroidButton_Click(sender As Object, e As EventArgs) Handles SendCentroidButton.Click
        Dim X(NumTriggerROIs - 1) As Integer
        Dim Y(NumTriggerROIs - 1) As Integer
        GetSLMspotCoors(ROICoordsX, X)
        GetSLMspotCoors(ROICoordsY, Y)
        Dim XX As String = String.Join(";", X)
        Dim YY As String = String.Join(";", Y)

        Dim Message2Send() As Byte = AddPrefixToMsg("X", XX)
        TCPClients.Client.Send(Message2Send.Concat(AddPrefixToMsg("Y", YY)).ToArray())



    End Sub

    Public Function AddPrefixToMsg(prefix As String, msg As String) As Byte()
        Dim ByteMessage() As Byte = System.Text.Encoding.ASCII.GetBytes(msg)
        Dim MsgLength As Integer = ByteMessage.Length

        Dim BytePrefix() As Byte = System.Text.Encoding.ASCII.GetBytes(prefix + MsgLength.ToString("D4"))
        Dim FullMsg() As Byte = BytePrefix.Concat(ByteMessage).ToArray()
        Return FullMsg

    End Function

    Private Sub GetSLMspotCoors(ByVal PVCoor() As Integer, ByRef SLMMcoor() As Integer)
        Dim this_zoom = CurrentZoom_NumericUpDown.Value
        If (this_zoom > 1) Then
            For i As Integer = 0 To NumTriggerROIs - 1
                SLMMcoor(i) = Math.Round((PVCoor(i) - 255.0) * 1.14 / this_zoom + 255)
            Next
        Else
            For i As Integer = 0 To NumTriggerROIs - 1
                SLMMcoor(i) = PVCoor(i)
            Next
        End If


    End Sub


    Private Sub SenStimInterval_NumericUpDown_ValueChanged(sender As Object, e As EventArgs) Handles SenStimInterval_NumericUpDown.ValueChanged
        NumFramesInterStim = SenStimInterval_NumericUpDown.Value
    End Sub

    Private Sub SendSTA_Button_Click(sender As Object, e As EventArgs) Handles SendSTA_Button.Click
        Dim Intensity(NumTriggerROIs - 1) As Integer
        For i As Integer = 0 To NumTriggerROIs - 1
            Intensity(i) = Math.Round(StaDff(i))
        Next

        Dim II As String = String.Join(";", Intensity)
        Dim Message2Send() As Byte = AddPrefixToMsg("I", II)
        TCPClients.Client.Send(Message2Send)

    End Sub

    Private Sub PictureBox2_MouseMove(ByVal sender As Object, ByVal e As System.Windows.Forms.MouseEventArgs) Handles PictureBox2.MouseMove

        lbl_xpos.Text = e.X.ToString
        lbl_ypos.Text = e.Y.ToString

    End Sub

    Private Sub ButtonBrowse_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonBrowse.Click
        Using dialogBrowse As New FolderBrowserDialog
            If dialogBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                TextBoxFilepath.Text = dialogBrowse.SelectedPath
                SaveFilePath = dialogBrowse.SelectedPath
            End If
        End Using
    End Sub
    Private Sub BrowseReadFile_Click(sender As Object, e As EventArgs) Handles BrowseReadFile.Click
        Dim fd As OpenFileDialog = New OpenFileDialog()
        fd.Title = "Open a text file"
        fd.InitialDirectory = "F:\Data\Zoe"
        fd.Filter = "Text Files|*.txt"
        fd.FilterIndex = 2
        fd.RestoreDirectory = True

        If fd.ShowDialog() = DialogResult.OK Then
            ReadFilePath = fd.FileName
            ReadFilePath_TextBox.Text = ReadFilePath
        End If
    End Sub




    Private Sub SaveButton_clicked(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button3.Click
        ' SAVE 
        Dim DataArray As String(,) = RecordArray
        Dim FileName As String
        If TboxFilename.Text.Trim.Length > 0 Then
            FileName = SaveFilePath & "\" & TboxFilename.Text & "_" & TboxAqNum.Text & ".txt"
        Else
            FileName = SaveFilePath & "\output_" & DateTime.Now.ToString("yyyyMMdd-HHmmss") & ".txt"
        End If
        Dim fstr As String = ""
        Dim sw As System.IO.StreamWriter = New System.IO.StreamWriter(FileName)

        For i As Int32 = DataArray.GetLowerBound(0) To DataArray.GetUpperBound(0)
            For j As Int32 = DataArray.GetLowerBound(1) To DataArray.GetUpperBound(1)
                fstr += DataArray(i, j) + ","
            Next
            sw.WriteLine(fstr)
            fstr = ""
        Next
        sw.Flush()
        sw.Close()

        ' SAVE TIMING 20180123
        Dim TimingDataArray As String() = TestPeriodArray
        Dim TimingFileName As String
        If TboxFilename.Text.Trim.Length > 0 Then
            TimingFileName = SaveFilePath & "\" & TboxFilename.Text & "_" & TboxAqNum.Text & "_timing.txt"
        Else
            TimingFileName = SaveFilePath & "\output_" & DateTime.Now.ToString("yyyyMMdd-HHmmss") & "_timing.txt"
        End If
        Dim Timing_fstr As String = ""
        Dim Timing_sw As System.IO.StreamWriter = New System.IO.StreamWriter(TimingFileName)

        For i As Int32 = TestPeriodArray.GetLowerBound(0) To TestPeriodArray.GetUpperBound(0)

            Timing_fstr += TestPeriodArray(i) + ","
            Timing_sw.WriteLine(Timing_fstr)
            Timing_fstr = ""
        Next
        Timing_sw.Flush()
        Timing_sw.Close()


    End Sub
    Private Sub ReadFile_Button_Click(sender As Object, e As EventArgs) Handles ReadFile_Button.Click
        ' first row is AllTriggerStatus (slm pattern)
        ' second row is NumFrames (stim frames)
        Try
            Dim MyReader As StreamReader = New StreamReader(ReadFilePath_TextBox.Text)
            Dim rowCount As Integer = 0
            Do While MyReader.Peek() >= 0
                Dim currentRow = MyReader.ReadLine()

                Dim tempStr() As String = currentRow.Split(",")
                Dim value As Int32
                Dim intArray = (From str In tempStr
                               Let isInt = Int32.TryParse(str, value)
                               Where isInt
                               Select Int32.Parse(str)).ToArray

                Select Case rowCount
                    Case 0
                        ReadAllTriggerStatus = intArray
                    Case 1
                        ReadStimFrames = intArray
                End Select
                rowCount += 1

            Loop

        Catch ex As Exception
            MsgBox(ex.Message)
        End Try

    End Sub

    Private Sub PlayBack_Button_Click(sender As Object, e As EventArgs) Handles PlayBack_Button.Click
        IsPlayBack = Not IsPlayBack
        If IsPlayBack Then
            PlayBack_Button.BackColor = Color.Green
            For i As Integer = NumControlFrames To TotalNumFrames - 1
                TrialStatus(i) = True
            Next

            ' check if the playback file has been loaded
            If ReadStimFrames.Count = 0 Or ReadAllTriggerStatus.Count = 0 Then
                MsgBox("Load txt file!")
            Else
                NumReadStimFrames = ReadStimFrames.Count
            End If
        Else
            PlayBack_Button.BackColor = Color.Gray
            For i As Integer = NumControlFrames To TotalNumFrames - 1
                TrialStatus(i) = False
            Next
        End If
    End Sub

    Private Sub UseGPU_CheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles UseGPU_CheckBox.CheckedChanged
        IfUseCuda = Not IfUseCuda
        NumBaselineSamples = 0
        BaselineAquired = False
        ReDim SumBaseline(NumTriggerROIs)
    End Sub

    Private Sub BrowseRefImg_Button_Click(sender As Object, e As EventArgs) Handles BrowseRefImg_Button.Click

        Dim fd As OpenFileDialog = New OpenFileDialog()
        fd.Title = "Open a tif file"
        fd.InitialDirectory = "E:\Data\Zoe"
        fd.Filter = "Image Files|*.TIF"
        fd.FilterIndex = 2
        fd.RestoreDirectory = True

        If fd.ShowDialog() = DialogResult.OK Then
            ReadRefImgPath = fd.FileName
            RefImgDir_Textbox.Text = ReadRefImgPath
        End If

    End Sub

    Private Sub ReadRefImage_Button_Click(sender As Object, e As EventArgs) Handles ReadRefImage_Button.Click
        Try

            ' read 16 bit image
            ' RefImgDir_Textbox.Text = "Z:\RTAOI\test images\20171121_OG233_s-002_Cycle00001_Ch2_000001.ome.tif" -- for debug
            Dim RefImage As Tiff = Tiff.Open(RefImgDir_Textbox.Text, "r")
            Dim width = RefImage.GetField(TiffTag.IMAGEWIDTH)
            Dim height = RefImage.GetField(TiffTag.IMAGELENGTH)
            Dim scanlineSize = RefImage.ScanlineSize


            Dim pixCount As Integer = 0
            Dim thisline(scanlineSize - 1) As Byte
            Dim testarray(512 * 512 - 1) As String
            For i As Integer = 0 To height(0).ToInt - 1
                RefImage.ReadScanline(thisline, i)
                For j As Integer = 0 To scanlineSize - 1 Step 2
                    RefGrayPixels(pixCount).real = thisline(j + 1) * 256 + thisline(j)
                    RefGrayPixels(pixCount).imag = 0.0
                    ' testarray(pixCount) = CStr(RefGrayPixels(pixCount).real) for debug
                    pixCount += 1
                Next
            Next

            '' for debug
            'Dim FileName As String = "Z:\RTAOI\test images\testArray.txt"
            'IO.File.WriteAllLines(FileName, testarray)
            '' end for debug

            FlagNewRefImageLoaded = True
        Catch
            MsgBox("read image failed!")
        End Try


    End Sub

    Private Sub GetShifts_CheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles GetShifts_CheckBox.CheckedChanged
        IfGetShifts = Not IfGetShifts
    End Sub


End Class