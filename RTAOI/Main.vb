' Closed-loop interface    20161219
' --- Instructions for Running: ---
' 1. make sure only one image acquisition channel is on in prairie view, image size 512x512, and image conversion to tiff is disabled
' 2. setup SLM control and sensory stimulation control
' 3. start RTAOI.exe 
' 4. select a .bmp image and select centres of ROIs 
' 5. setup experiment type, parameters and TCP connections in the UI
' 6. check 'display-off' box and click start-experiment
' 7. experiment will start as Prairie View starts imaging
' 8. save the stimulation frame and pattern indices to a .txt file 
' --- End of Instructions ---

Option Strict Off
Imports System.Runtime.InteropServices
Imports System.Math
Imports System.Threading
Imports System.Linq
Imports System.Net 'TCP

Public Class Main
    ' change the parameters here 
    Dim TCPClients As Sockets.TcpClient
    Dim TCPClientStream As Sockets.NetworkStream
    Dim fmt As String = "00"
    Private AllResetFrames As Integer = 2 ' ignore photostim-contaminated frames
    'ROIs
    Private NumTriggerROIs As Integer = 3  ' number of trigger rois in experiment 
    Private SelectedROIsCount As Integer = 0  'used for start, count numebr of rois as they are selected
    Private MaskRadius As Integer = 10
    Private ROIMasks As New List(Of Image)
    Private ROIMaskIndices(NumTriggerROIs) As Queue(Of Integer)
    Private AllMaskROIArray(512 * 512 - 1) As Integer
    Private SumROIintensity(NumTriggerROIs - 1) As Integer

    Private ROIMaskIndicesX(NumTriggerROIs) As Queue(Of Integer)
    Private ROIMaskIndicesY(NumTriggerROIs) As Queue(Of Integer)
    Private ROICoordsX(NumTriggerROIs) As Integer
    Private ROICoordsY(NumTriggerROIs) As Integer
    Private ROICoords(NumTriggerROIs) As Integer
    Private NormaliseTraces As Boolean
    Private RefMagnification As Single = 1.14      'zoom used for generating SLM phase mask

    'startup bits
    Private SamplesReceived As Long
    Private ExperimentRunning As Boolean
    Private AllROIsSelected As Boolean
    Private TimeStarted As Long
    Private LockThresholds As Boolean
    Private FireThisWhenAboveCheck(NumTriggerROIs - 1) As CheckBox
    Private FireThisWhenAboveLabel(NumTriggerROIs - 1) As Label
    Private FireThisWhenBelowCheck(NumTriggerROIs - 1) As CheckBox
    Private FireThisWhenBelowLabel(NumTriggerROIs - 1) As Label
    Private StartRecording As Boolean = False

    'data
    Private SlidingWindowSize As Integer = 60 'in samples (frames), this is the size of the buffer holding the data (ROI traces)
    Private TempArrayForPlot(SlidingWindowSize - 1) As Double
    Private RoundTempArray(SlidingWindowSize - 1) As Double
    Private SlidingWindowArray(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single
    Private ROIThreshold(NumTriggerROIs - 1) As Single
    Private threshLabel(NumTriggerROIs - 1) As Label
    Private threshSpinbox(NumTriggerROIs - 1) As NumericUpDown
    Private averageLabel(NumTriggerROIs - 1) As Label
    Private averageValue(NumTriggerROIs - 1) As Label
    Private SumPixelValue As Long
    Private NumPixelsForROI As Integer
    Private CurrentValue As Single
    Private CurrentRatio As Single
    Private LastRatio(NumTriggerROIs - 1) As Single

    Private av As Single
    Private sd As Single
    Private NumExperiment As Integer = 1
    '============== for baseline ===================
    Private ROIBaseline(NumTriggerROIs - 1) As Single
    Private SumBaseline(NumTriggerROIs - 1) As Single
    Private NumBaselineSamples As Integer = 0
    Private NumBaselineSpRequired As Integer = 150
    Private BaselineAquired As Boolean = False

    '================ for multi-cell rate clamp===========

    Private NumWaitContinuousFrames As Integer = 2
    Private AllTriggerStatus As Integer = 0  ' SLM Phase-mask index.
    Private TriggerTable(NumTriggerROIs - 1) As Integer
    Private DisplayOn As Boolean = True

    '===== for recording 
    Private TotalNumFrames As Integer = 14400
    Private TrialStatus(30000 - 1) As Boolean

    '==== for display 
    Private flipdone As Boolean = False

    'daq output
    'Private daqtask As New Task
    Private daqtaskS1 As New Task  ' Dev6 ao0
    Private daqtaskS2 As New Task   ' Dev6 ao1 toPV
    Private writerS1 As New AnalogSingleChannelWriter(daqtaskS1.Stream)
    Private writerS2 As New AnalogSingleChannelWriter(daqtaskS2.Stream)
    Private OutputArrayLength As Integer = 15 * NumTriggerROIs - 1
    Private OutputArray(1, OutputArrayLength) As Double
    Private ToPVreset(5) As Double
    Private ToPVtrigger(10) As Double
    Private ClampOnArray(5) As Double
    Private ClampOffArray(5) As Double
    Private TestOutputArray(1, 10) As Double
    Private TriggersToSLM(NumTriggerROIs, OutputArrayLength) As Double  
    Private TriggerToPV(NumTriggerROIs, OutputArrayLength) As Double  
    Private TriggerEnabled(NumTriggerROIs - 1) As Boolean
    Private AllTriggersEnabled As Boolean
    Private LastTrigger As Long
    Private LastTriFrame As Long

    Private RecordArray(2, 12000) As String
    Private StimNum As Integer
    Private NumFrames As Integer = 0
    Private LastStimFrame(NumTriggerROIs - 1) As Integer
    Private CurrentSLMPattern As Integer = 0 'keep track of SLM pattern 
    Private BlankXSamplesPostStim As Integer = 0
    Private filepath As String = "F:\Zoe"      'default save file path


    'daq digital output
    'Private daqtaskD As New Task
    'Private writerD As New DigitalMultiChannelWriter(daqtaskD.Stream)
    'Private DigitalOutputArray(1, 1) As Byte


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
	
    ' activity clamp
    Private IsCalciumClamp As Boolean = False
    Private NumClampOnFrames As Integer = 900
    Private NumClamps As Integer = 1
    Private NumClampIntervalFrames As Integer = 900

    ' sensory stim
    Dim SensoryStimType As String = "1"
    Dim TCPClients2 As Sockets.TcpClient
    Dim TCPClientStream2 As Sockets.NetworkStream
    Dim sendbytes2() As Byte = System.Text.Encoding.ASCII.GetBytes("1")
    Private IsMaster As Boolean = True
    Private IsSlave As Boolean = False
    Private IsSensoryStim As Boolean = False
    Private NumFramesPostStim As Integer = 0
    Private NumFramesPosttimRequired As Integer = 100
    Private SenStimDetected As Boolean = False
    Private SenStimStartFrame As Integer = 0
    Private RecordSenStimStarted(0, 200) As String
    Private NumSenStim As Integer = 0
    Private ToSensoryStim(5) As Double
    Private NumSensoryControlFrames As Integer = 900
    Private NumStimFrames As Integer = 20 'number of frames with intervene after sensory stim starts
    Private NumFramesInterStim As Integer = 300 ' 
    Private TotalSensoryStims As Integer = 20

    ' trigger-target experiment
    Private IsTrigTar As Boolean = False
    Private TrgCellIdx As Integer = 0
    Private ROItoTrig As Integer = 0
    Private SlidingAvg(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single
    Private SlidingStd(NumTriggerROIs - 1, SlidingWindowSize - 1) As Single
    Private ROIav(NumTriggerROIs - 1) As Single
    Private ROIstd(NumTriggerROIs - 1) As Single
    Private LastTrigRatio(NumTriggerROIs - 1) As Single
    Private WaitFramesAfterTrig As Integer = 30
    Private N As Integer = 2
    Private tempidx(NumTriggerROIs - 1) As Integer

    ' get noise std during control period
    Private NoiseStd(NumTriggerROIs - 1) As Single
    Private NumNoiseStdFrames(NumTriggerROIs - 1) As Integer
    Private NumControlFrames As Integer = 3000

    Private LastTriThresh(NumTriggerROIs - 1) As Integer
    Private NumPostStim(NumTriggerROIs - 1) As Integer

    ' wait for another frame before triggering photostim
    Private TriggerOnFlag(NumTriggerROIs - 1) As Integer
    Private TriggerOnFrame(NumTriggerROIs - 1) As Integer
    Private EnableLPF As Boolean = False

    '' parameters for test  
    'Private ExperimentRunning As Boolean = True
    Private displaytime As Long = 0
    Private ThisFramePeriod As Long = 0
    Private TestPeriod As Long = 0
    Private DisplayOffPeriod As Integer = 5    ' number of frames every display-off period
    Private DisplayOffCounter As Integer = 0


    Private Sub OpenImage()
        'open an image to select ROIs
        OpenFileDialog1.Filter = "BMP |*.bmp| All Files|*.*"
        OpenFileDialog1.FileName = ""
        If OpenFileDialog1.ShowDialog(Me) = DialogResult.OK Then
            Dim ImgPath As String = OpenFileDialog1.FileName
            PictureBox1.Image = System.Drawing.Bitmap.FromFile(ImgPath)
        End If
    End Sub

    Private Sub zoomChange(ByVal Magnification As Single)     ' works for 'display-on' mode

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
                '' Check indices
                'Dim CustomPen2 As New Pen(Color.Yellow)
                'For i As Integer = 0 To ROIMaskIndices(SelectedROIsCount).Count()
                '    g.DrawRectangle(CustomPen2, ROIMaskIndicesX(SelectedROIsCount)(i), ROIMaskIndicesY(SelectedROIsCount)(i), 1, 1)
                'Next


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
        'connect to prairie view  
        ' uncomment from here
        m_pl.Connect()
        m_pl.SendScriptCommands("-dw")
        m_pl.SendScriptCommands("-lbs true 0")
        m_pl.SendScriptCommands("-srd True 25")
        m_started = True
        m_paused = Not m_paused
        ' uncomment ends
       

        ' ------------ test only --------------
        'm_oTimer.Start()
        'Dim testi As Integer = 1
        'daqtask.AOChannels.CreateVoltageChannel("Dev6/ao0:1", "", -10.0, 10.0, AOVoltageUnits.Volts)
        'For i As Integer = 0 To 9
        '    ToPVOutputArray(1, i) = 5
        'Next
        'While (testi < 10)
        '    'Dim iSamples As Integer
        '    'Dim iBuffer() As Int16 = m_pl.ReadRawDataStream(iSamples)
        '    writer.BeginWriteMultiSample(True, ToPVOutputArray, Nothing, Nothing)
        '    'm_pl.SendScriptCommands("-mp Zoe Test3reps")
        '    'm_pl.SendScriptCommands("-mp")
        '    'm_pl.SendScriptCommands("-mp .5 .5 10 Fidelity 250 true .032 3 ")

        '    m_pl.SendScriptCommands("-mp .5 .5 10 Fidelity 250 true .032 3 1 .5 .5 10 Fidelity 250 true .032 3")
        '    LastTrigger = m_oTimer.ElapsedMilliseconds
        '    While (m_oTimer.ElapsedMilliseconds - LastTrigger < 500)

        '    End While
        '    testi = testi + 1
        '    'm_pl.SendScriptCommands("-srd True 25")
        'End While

        ' ----------- end test only ------------

        'Load an image for the FOV
        'can be useful to predetermine trigger locations rather than finding them in the live stream view
        OpenImage()  'Load an image for the FOV 

        ' set up picture boxes (for FOV and ROI display)
        PictureBox2.Parent = PictureBox1
        PictureBox2.Location = New Point(0, 0)
        PictureBox2.BackColor = Color.Transparent

        Dim bmp As New Drawing.Bitmap(512, 512)
        PictureBox2.Image = bmp

        StatusText.Text = "Select " & Convert.ToString(NumTriggerROIs - SelectedROIsCount) & " trigger ROI(s)"

        For i As Integer = 0 To NumTriggerROIs - 1
            'Initialise ROI mask indices array(queue because unknown size)
            ROIMaskIndices(i) = New Queue(Of Integer)()
            ROIMaskIndicesX(i) = New Queue(Of Integer)()
            ROIMaskIndicesY(i) = New Queue(Of Integer)()

            'Set x limit of graphs
            If i < 4 Then
                Chart1.ChartAreas(i).AxisX.Maximum = SlidingWindowSize

                'Add some components to gui, up to 8 ROIs
                threshLabel(i) = New Label
                threshLabel(i).Text = "Threshold"
                threshLabel(i).Size = New Size(100, 13)
                threshLabel(i).Left = 1030
                threshLabel(i).Top = 30 + (i * 125)
                threshSpinbox(i) = New NumericUpDown
                threshSpinbox(i).Maximum = 999999
                threshSpinbox(i).Minimum = -999999
                threshSpinbox(i).Tag = i
                AddHandler threshSpinbox(i).ValueChanged, AddressOf ThresholdValueChanged
                threshSpinbox(i).Value = 0
                threshSpinbox(i).Size = New Size(100, 13)
                threshSpinbox(i).Left = 1030
                threshSpinbox(i).Top = 50 + (i * 125)
                threshSpinbox(i).DecimalPlaces = 2
                averageLabel(i) = New Label
                averageLabel(i).Text = "Average"
                averageLabel(i).Size = New Size(100, 13)
                averageLabel(i).Left = 1030
                averageLabel(i).Top = 70 + (i * 125)
                averageValue(i) = New Label
                averageValue(i).Text = "0"
                averageValue(i).Size = New Size(100, 13)
                averageValue(i).Left = 1030
                averageValue(i).Top = 90 + (i * 125)
                FireThisWhenAboveCheck(i) = New CheckBox
                FireThisWhenAboveCheck(i).Left = 1070
                FireThisWhenAboveCheck(i).Top = 105 + (i * 125)
                FireThisWhenAboveLabel(i) = New Label
                FireThisWhenAboveLabel(i).Left = 1030
                FireThisWhenAboveLabel(i).Top = 110 + (i * 125)
                FireThisWhenAboveLabel(i).Text = "Above"
                FireThisWhenBelowCheck(i) = New CheckBox
                FireThisWhenBelowCheck(i).Left = 1130
                FireThisWhenBelowCheck(i).Top = 105 + (i * 125)
                FireThisWhenBelowLabel(i) = New Label
                FireThisWhenBelowLabel(i).Left = 1090
                FireThisWhenBelowLabel(i).Top = 110 + (i * 125)
                FireThisWhenBelowLabel(i).Text = "Below"
            End If
            If i >= 4 Then
                Dim j As Integer = i - 4
                Chart2.ChartAreas(j).AxisX.Maximum = SlidingWindowSize

                'Add some components to gui
                threshLabel(i) = New Label
                threshLabel(i).Text = "Threshold"
                threshLabel(i).Size = New Size(100, 13)
                threshLabel(i).Left = 1550
                threshLabel(i).Top = 30 + (j * 125)
                threshSpinbox(i) = New NumericUpDown
                threshSpinbox(i).Maximum = 999999
                threshSpinbox(i).Minimum = -999999
                threshSpinbox(i).Tag = i
                AddHandler threshSpinbox(i).ValueChanged, AddressOf ThresholdValueChanged
                threshSpinbox(i).Value = 0
                threshSpinbox(i).Size = New Size(100, 13)
                threshSpinbox(i).Left = 1550
                threshSpinbox(i).Top = 50 + (j * 125)
                threshSpinbox(i).DecimalPlaces = 2
                averageLabel(i) = New Label
                averageLabel(i).Text = "Average"
                averageLabel(i).Size = New Size(100, 13)
                averageLabel(i).Left = 1550
                averageLabel(i).Top = 70 + (j * 125)
                averageValue(i) = New Label
                averageValue(i).Text = "0"
                averageValue(i).Size = New Size(100, 13)
                averageValue(i).Left = 1550
                averageValue(i).Top = 90 + (j * 125)
                FireThisWhenAboveCheck(i) = New CheckBox
                FireThisWhenAboveCheck(i).Left = 1590
                FireThisWhenAboveCheck(i).Top = 105 + (j * 125)
                FireThisWhenAboveLabel(i) = New Label
                FireThisWhenAboveLabel(i).Left = 1550
                FireThisWhenAboveLabel(i).Top = 110 + (j * 125)
                FireThisWhenAboveLabel(i).Text = "Above"
                FireThisWhenBelowCheck(i) = New CheckBox
                FireThisWhenBelowCheck(i).Left = 1650
                FireThisWhenBelowCheck(i).Top = 105 + (j * 125)
                FireThisWhenBelowLabel(i) = New Label
                FireThisWhenBelowLabel(i).Left = 1610
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

        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerEnabled(i) = True
        Next

        '================= build triggers =============== 
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

        '================ End build array 20160330  ======================
     
        ''-- for trigger-target
        For i As Integer = NumControlFrames To TotalNumFrames - 1
            TrialStatus(i) = True
        Next

        ' for multi-clamping
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerTable(i) = Math.Pow(2, i)
        Next

        ' DAQ config
        'daqtask.AOChannels.CreateVoltageChannel("Dev6/ao0:1", "", -10.0, 10.0, AOVoltageUnits.Volts)

        daqtaskS1.AOChannels.CreateVoltageChannel("Dev6/ao0", "", -10.0, 10.0, AOVoltageUnits.Volts)
        daqtaskS2.AOChannels.CreateVoltageChannel("Dev6/ao1", "", -10.0, 10.0, AOVoltageUnits.Volts)
        daqtaskS1.Timing.SampleClockRate = 10000
        daqtaskS2.Timing.SampleClockRate = 10000
        daqtask2.AIChannels.CreateVoltageChannel("Dev6/ai1", "readChannel", AITerminalConfiguration.Differential, 0.0, 5.0, AIVoltageUnits.Volts)
        'daqtask.Timing.SampleClockRate = 10000
        
		'start thread
        chkFlipOdd.Checked = True
        m_oUpdateThread.Priority = ThreadPriority.AboveNormal
        m_oUpdateThread.Start()
    End Sub

    Private Sub UpdateImage()
        m_oTimer.Start()
        Dim iLastElapsedMilliseconds As Long = 0
        Dim iPollStart As Long
        While Not m_closing

            While m_paused
                ' do nothing
            End While

            Dim iSamples As Integer
            iPollStart = m_oTimer.ElapsedMilliseconds

            Dim iBuffer() As Int16 = m_pl.ReadRawDataStream(iSamples)     'when isamples > 3*512*512  error at marshal.copy 20160515
            If iSamples = 0 Or iSamples <> 786432 Then Continue While
            m_fPollingTime2 += m_oTimer.ElapsedMilliseconds - iPollStart
            m_iPollingIterations2 += 1

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
        Static iLastElapsedMilliseconds As Long = 0
        Dim iFrameSize As Integer = m_iImageSize * m_iImageSize * m_iSamplesPerPixel
        Dim iExtraSamples = Max(0, (samples \ iFrameSize) - 1) * iFrameSize  ' don't bother processing multiple frames for one display update
        Dim iDataOffset As Integer = iExtraSamples
        Dim processedGrayPixels((samples \ m_iSamplesPerPixel) - 1) As Integer
        '********************************************************************************************************************
        '                                     GET ROI AVG INTENSITY FROM BUFFER    
        '********************************************************************************************************************
        Dim teststart As Long = m_oTimer.ElapsedMilliseconds

        '==================================             Display on             ==============================================
        If DisplayOn = True Or (DisplayOffCounter = DisplayOffPeriod - 1 And ExperimentRunning = False) Then   ' uncomment this for experiment
            'If DisplayOn = True Then       ' test only
            Dim iRgbBuffer() As Integer = ConvertGray16ToRGB(buffer, iExtraSamples, samples - iExtraSamples, processedGrayPixels)
            Dim iSamplesToWrite = iRgbBuffer.Length
            Dim oData As Imaging.BitmapData = m_oBitmap.LockBits(New Rectangle(0, 0, m_iImageSize, m_iImageSize), Imaging.ImageLockMode.ReadWrite, Imaging.PixelFormat.Format32bppRgb)

            iFrameSize = m_iImageSize * m_iImageSize
            iExtraSamples = Max(0, (iSamplesToWrite \ iFrameSize) - 1) * iFrameSize  ' don't bother processing multiple frames for one display update
            iSamplesToWrite -= iExtraSamples

            'flip the rows, correct for bidirectional scanning
            If m_flipOdd OrElse m_flipEven Then
                Dim iTemp As Integer
                Dim iFlipOffset = iDataOffset
                Dim iLeftoverSamples As Integer = m_iTotalSamples Mod m_iImageSize
                If iLeftoverSamples > 0 Then iFlipOffset += m_iImageSize - iLeftoverSamples ' skipping partial lines to make this easier (resonant mode always provides full lines)
                Dim odd As Boolean = (m_iTotalSamples \ m_iImageSize) Mod 2 = 0
                While iFlipOffset <= iRgbBuffer.Length - m_iImageSize
                    If (m_flipOdd AndAlso odd) OrElse (m_flipEven AndAlso Not odd) Then
                        For iIndex As Integer = iFlipOffset To iFlipOffset + (m_iImageSize \ 2) - 1
                            iTemp = iRgbBuffer(iIndex)
                            iRgbBuffer(iIndex) = iRgbBuffer(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset))
                            iRgbBuffer(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset)) = iTemp

                            iTemp = processedGrayPixels(iIndex)
                            processedGrayPixels(iIndex) = processedGrayPixels(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset))
                            processedGrayPixels(iFlipOffset + m_iImageSize - 1 - (iIndex - iFlipOffset)) = iTemp

                        Next
                    End If
                    odd = Not odd
                    iFlipOffset += m_iImageSize
                End While
            End If
            'put data into image
            'Dim cropRgbBuffer(iFrameSize - 1) As Integer
            'Array.Copy(iRgbBuffer, cropRgbBuffer, cropRgbBuffer.Length)

            While iSamplesToWrite > 0
                Dim iSamplesToCopy As Integer = Min(iSamplesToWrite, iFrameSize - m_iTotalSamples)
                If iSamplesToCopy > 0 Then
                    Marshal.Copy(iRgbBuffer, iDataOffset, New IntPtr(oData.Scan0.ToInt64 + m_iTotalSamples * 4), iSamplesToCopy)
                    m_iTotalSamples += iSamplesToCopy
                    If m_iTotalSamples >= iFrameSize Then m_iTotalSamples -= iFrameSize
                    iSamplesToWrite -= iSamplesToCopy
                    iDataOffset += iSamplesToCopy
                End If
            End While
            m_oBitmap.UnlockBits(oData)
        End If
        '================================== End of display on ============================================
		'==================================   Display off   ==============================================

        If DisplayOn = False Then ' test direct processing on buffer
            'flip the rows, correct for bidirectional scanning    flip ROI masks
            If flipdone = False Then
                If m_flipOdd OrElse m_flipEven Then
                    Dim iTemp As Integer
                    Dim iFlipOffset = iDataOffset
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
            ProcessGray16(buffer, iExtraSamples, samples - iExtraSamples, AllMaskROIArray, processedGrayPixels)   ' uncomment this for experiment
            ' -- for test only --
            'Dim iRgbBuffer() As Integer = ProcessGray16(buffer, iExtraSamples, samples - iExtraSamples, AllMaskROIArray, processedGrayPixels)
            'Dim iSamplesToWrite = iRgbBuffer.Length
            'Dim oData As Imaging.BitmapData = m_oBitmap.LockBits(New Rectangle(0, 0, m_iImageSize, m_iImageSize), Imaging.ImageLockMode.ReadWrite, Imaging.PixelFormat.Format32bppRgb)
            'While iSamplesToWrite > 0
            '    Dim iSamplesToCopy As Integer = Min(iSamplesToWrite, iFrameSize - m_iTotalSamples)
            '    If iSamplesToCopy > 0 Then
            '        Marshal.Copy(iRgbBuffer, iDataOffset, New IntPtr(oData.Scan0.ToInt64 + m_iTotalSamples * 4), iSamplesToCopy)
            '        m_iTotalSamples += iSamplesToCopy
            '        If m_iTotalSamples >= iFrameSize Then m_iTotalSamples -= iFrameSize
            '        iSamplesToWrite -= iSamplesToCopy
            '        iDataOffset += iSamplesToCopy
            '    End If
            'End While
            'm_oBitmap.UnlockBits(oData)
            ' -- end test only --


        End If   ' end if displayon is false

        '  ************************            END  GET ROI AVG INTENSITY FROM BUFFER             ***********************************


        Dim teststop As Long = m_oTimer.ElapsedMilliseconds
        displaytime = teststop - teststart
        testdisplaytimeLabel.Text = CStr(displaytime)

        'Here is where the realtime threshold checks and feedback/triggering happens:

        If AllROIsSelected Then
            'If IsSensoryStim AndAlso IsMaster Then

            '    If NumFrames < NumSensoryControlFrames Then
            '        Return
            '    End If
            'End If
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

            If NumBaselineSamples = NumBaselineSpRequired Then

                For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                    ROIBaseline(ROIIdx) = SumBaseline(ROIIdx) / NumBaselineSpRequired
                    If DisplayOn = False Then
                        LastRatio(ROIIdx) = SumROIintensity(ROIIdx)
                    End If

                Next
                NumBaselineSamples += 1
                BaselineAquired = True
                AllTriggersEnabled = True

                If IsSensoryStim = True AndAlso IsMaster AndAlso NumSenStim <= TotalSensoryStims Then
                    writerS1.BeginWriteMultiSample(True, ToSensoryStim, Nothing, Nothing) 'write to channel a00
                    TCPClients2.Client.Send(sendbytes2)
                    NumSenStim += 1
                End If

            End If

            '============================= Compute F and threshold   ===================================
            If BaselineAquired Then
                SamplesReceived += 1

                '------------------ Wait for sensory trigger ---------------------------
                ' RTAOI as slave; triggers (for sensory-on) should > 60 ms duration
                If IsSensoryStim = True Then
                    If IsMaster Then
                        SenStimDetected = True
                    End If
                    AllTriggersEnabled = False
                    If SenStimDetected = False Then
                        Dim readerData As Double = reader.ReadSingleSample
                        lblAiVoltage.Text = Format(readerData, "0.00")
                        ' detect sensory stim trigger 
                        If readerData > 0.5 Then
                            SenStimDetected = True
                            NumFramesPostStim = 0
                            SenStimStartFrame = NumFrames
                            RecordSenStimStarted(0, NumSenStim) = SenStimStartFrame
                            NumSenStim += 1
                        End If
                    Else
                        NumFramesPostStim += 1
                    End If
                    If NumFramesPostStim > NumFramesPosttimRequired AndAlso NumFramesPostStim < NumStimFrames + NumFramesPosttimRequired Then
                        AllTriggersEnabled = True

                    End If
                    If NumFramesPostStim > NumFramesPosttimRequired + NumFramesInterStim Then 'update baseline
                        BaselineAquired = False
                        SenStimDetected = False
                        NumBaselineSamples = 0
                        NumFramesPostStim = 0
                        ReDim SumBaseline(NumTriggerROIs - 1)


                    End If
                    lblNoFramesPostStim.Text = CStr(NumFramesPostStim)
                    lblNumSenStim.Text = CStr(NumSenStim)


                End If
                '---------------------End Wait for sensory trigger ---------------------------------------------------

                If DisplayOn = False And AllTriggersEnabled = True Then
                    AllTriggerStatus = 0
                    For ROIIdx As Integer = 0 To NumTriggerROIs - 1
                        NumPostStim(ROIIdx) += 1

                        If IsTrigTar = True Then  ' trigger-target 
                            Dim CurrentStd As Double
                            ' -------  get current mean and sd -------------
                            'Dim tempidx = SamplesReceived Mod SlidingWindowSize
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
                            '=====get noise std end ===============
                            'RatioThresh = (ROIThreshold(ROIIdx) / ROIBaseline(ROIIdx) - 1) * 100 ' for test
                            lblCurrentThresh.Text = Format(ROIThreshold(ROIIdx), "0.0")
                            If CheckBoxNoiseConstraint.Checked = True AndAlso (TrialStatus(NumFrames - 1) = True) AndAlso (CurrentStd < NoiseStd(ROIIdx)) Then
                                Continue For
                            End If
                            '-------- end get current mean and sd -----------

                        Else ' if not Tri-target, get df/f
                            CurrentRatio = ((SumROIintensity(ROIIdx) - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100
                            lblcurrentdf.Text = Format(CurrentRatio, "0.0")
                        End If ' end if IsTrigTar


                        If TriggerEnabled(ROIIdx) = False AndAlso NumPostStim(ROIIdx) > WaitFramesAfterTrig Then
                            TriggerEnabled(ROIIdx) = True
                        End If

                        '-------------------- Compare with thresh -------------------------
                        If (EnableLPF = False) Then ' lPF off
                            If FireThisWhenAboveCheck(ROIIdx).Checked = True And CurrentRatio > ROIThreshold(ROIIdx) And TriggerEnabled(ROIIdx) = True Then

                                ROItoTrig = ROIIdx
                                AllTriggerStatus += TriggerTable(ROIIdx)
                                LastTrigRatio(ROIIdx) = CurrentRatio
                                TriggerEnabled(ROIIdx) = False
                                LastStimFrame(ROIIdx) = NumFrames
                                NumPostStim(ROIIdx) = 0

                            End If

                            If FireThisWhenBelowCheck(ROIIdx).Checked = True And CurrentRatio < ROIThreshold(ROIIdx) Then
                                AllTriggerStatus += TriggerTable(ROIIdx)
                            End If
                        Else  ' LPF on: wait for another frame after an event is detected
                            LPFlogic(ROIIdx)
                        End If


                        '------------------ End Compare with thresh --------------------

                    Next ' next ROI
                    DisplayOffCounter += 1
                End If ' display = false andalso alltriggerenabled = true

                If DisplayOn = True Or (DisplayOffCounter = DisplayOffPeriod And ExperimentRunning = False) Then
                    DisplayOnLogic(processedGrayPixels)
                End If ' end if display on is true

                If NumFrames - LastTriFrame >= AllResetFrames Then ' take care of photostim artefects
                    AllTriggersEnabled = True
                    StatusText.Text = ""
                End If
                '-------------------- send clamp on or off trigger ------------------------------------
                If (IsCalciumClamp) Then
                    RecordArray(2, NumFrames) = CStr(CurrentRatio)
                    If TrialStatus(NumFrames - 2) = False AndAlso TrialStatus(NumFrames - 1) = True Then
                        writerS1.BeginWriteMultiSample(True, ClampOnArray, Nothing, Nothing) 'write to channel a00
                    End If
                    If TrialStatus(NumFrames - 2) = True AndAlso TrialStatus(NumFrames - 1) = False Then
                        writerS1.BeginWriteMultiSample(True, ClampOffArray, Nothing, Nothing) 'write to channel a00
                    End If
                End If

                '--------------------------------------------------------------------------------------
                '================================= TRIGGER  PHOTOSTIM ================================
                If ExperimentRunning AndAlso AllTriggerStatus > 0 Then 'AndAlso NumFrames > NumControlFrames Then
                    'If EnableLPF AndAlso IsTrigTar Then  'stim targets assigned to one trigger ROItoTrig
                    '    DeliverTriggers(ROItoTrig)
                    'Else                                 'stim combination of targets
                    DeliverTriggers(AllTriggerStatus)
                    AllTriggerStatus = 0
                End If

            End If ' end if numofbaseline > numrequired

        End If 'end if AllROIsSelected

        ' refresh live image window
        If DisplayOn = True Then
            PictureBox1.Refresh()
        End If
        ' refresh labels
        lblCurrentRatio.Text = Format(CurrentRatio, "0.0")
        ' measure timing
        Dim iElapsedMilliseconds As Long = m_oTimer.ElapsedMilliseconds
        TestPeriod = iElapsedMilliseconds - teststop   ' processing time
        ThisFramePeriod = iElapsedMilliseconds - iLastElapsedMilliseconds
        iLastElapsedMilliseconds = iElapsedMilliseconds
        m_iProcessingIterations += 1
        lblPollingTime.Text = Format(m_fPollingTime2 / m_iPollingIterations2, "0.000")
        lblPollingPeriod.Text = Format(m_fPollingTime / m_iPollingIterations, "0.000")
        lblFrameTime.Text = Format(ThisFramePeriod, "0.000")
        lblTestTime.Text = Format(TestPeriod, "0.000")

    End Sub


    Private Sub DisplayOnLogic(ByRef processedGrayPixels As Integer())
        'If DisplayOn = True Or ExperimentRunning = False Then  ' test only
        AllTriggerStatus = 0
        DisplayOffCounter = 0
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
                    If DisplayOn = False Then
                        CurrentValue = ((SumROIintensity(ROIIdx) - ROIBaseline(ROIIdx)) / ROIBaseline(ROIIdx)) * 100
                    End If
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
                    'If DataIdx > SlidingWindowSize - 1 - 30 Then
                    '    Prev30Values(DataIdx - SlidingWindowSize + 30) = SlidingWindowArray(ROIIdx, DataIdx)
                    'End If
                    SlidingWindowArray(ROIIdx, DataIdx) = SlidingWindowArray(ROIIdx, DataIdx + 1)
                End If
                TempArrayForPlot(DataIdx) = SlidingWindowArray(ROIIdx, DataIdx)  'temp array to be loaded into graph
                RoundTempArray(DataIdx) = Round(TempArrayForPlot(DataIdx), 2)
            Next

            ' update graph data

            'For i As Integer = 0 To SlidingWindowSize - 1
            '    RoundTempArray(i) = Round(TempArrayForPlot(i), 4)
            'Next
            If ROIIdx < 4 Then
                Chart1.Series(ROIIdx).Points.DataBindY(RoundTempArray)

            Else
                Chart2.Series(ROIIdx - 4).Points.DataBindY(RoundTempArray)
            End If

            ' update thresholds
            If LockThresholds = False Then
                av = TempArrayForPlot.Average()
                sd = getStandardDeviation(TempArrayForPlot)

                averageValue(ROIIdx).Text = CStr(av)
                threshSpinbox(ROIIdx).Value = CDec(av + (4 * sd))  ' threshold is X*SD
            End If
        Next
        ' refresh live graphs
        Chart1.Refresh()
        Chart2.Refresh()

    End Sub
    Private Sub LPFlogic(ByRef ROIIdx As Integer)
        If FireThisWhenAboveCheck(ROIIdx).Checked = True AndAlso TriggerEnabled(ROIIdx) = True Then
            If (TriggerOnFlag(ROIIdx) = 1) Then
                If (CurrentRatio > LastTriThresh(ROIIdx)) Then
                    TriggerOnFrame(ROIIdx) += 1
                Else
                    TriggerOnFlag(ROIIdx) = 0
                    TriggerOnFrame(ROIIdx) = 0
                End If
            End If
            If (TriggerOnFrame(ROIIdx) = NumWaitContinuousFrames) Then ' change 2 to other filter size
                TriggerOnFlag(ROIIdx) = 0
                TriggerOnFrame(ROIIdx) = 0

                ROItoTrig = ROIIdx
                AllTriggerStatus += TriggerTable(ROIIdx)
                LastTrigRatio(ROIIdx) = CurrentRatio
                TriggerEnabled(ROIIdx) = False
            End If

            If (TriggerOnFlag(ROIIdx) = 0) AndAlso CurrentRatio > ROIThreshold(ROIIdx) Then
                TriggerOnFlag(ROIIdx) = 1
                TriggerOnFrame(ROIIdx) = 1
                LastTriThresh(ROIIdx) = ROIThreshold(ROIIdx)
            End If

            lblTrigOnFlag.Text = CStr(TriggerOnFrame(ROIIdx))
        End If
        If FireThisWhenBelowCheck(ROIIdx).Checked = True AndAlso TriggerEnabled(ROIIdx) = True Then
            If (TriggerOnFlag(ROIIdx) = 1) Then
                If (CurrentRatio < ROIThreshold(ROIIdx)) Then
                    TriggerOnFrame(ROIIdx) += 1
                Else
                    TriggerOnFlag(ROIIdx) = 0
                    TriggerOnFrame(ROIIdx) = 0
                End If
            End If
            If (TriggerOnFrame(ROIIdx) = NumWaitContinuousFrames) Then ' change 2 to other filter size
                TriggerOnFlag(ROIIdx) = 0
                TriggerOnFrame(ROIIdx) = 0
                AllTriggerStatus += TriggerTable(ROIIdx)
                TriggerEnabled(ROIIdx) = False
            End If

            If (TriggerOnFlag(ROIIdx) = 0) AndAlso CurrentRatio < ROIThreshold(ROIIdx) Then
                TriggerOnFlag(ROIIdx) = 1
                TriggerOnFrame(ROIIdx) = 1
            End If

            lblTrigOnFlag.Text = CStr(TriggerOnFrame(ROIIdx))
        End If

    End Sub
    Private Sub ProcessGray16(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByVal ROImasks() As Integer, ByRef processedGrayPixels() As Integer)  ' delete as integer ()
        Dim rgbPixels((samples \ m_iSamplesPerPixel) - 1) As Integer
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

                    ' -- for test only 
                    '    Dim forRGB As Integer = processedGrayPixels(i) >> m_iBitsToShift
                    '    ' put the values into r,g and b channels
                    '    rgbPixels(i) = CInt(forRGB << 16) Or CInt(forRGB << 8) Or CInt(forRGB)
                    '    rgbPixels(i) = forRGB Or (forRGB << 8) Or (forRGB << 16)
                    'Else
                    '    rgbPixels(i) = 0
                    ' -- end test only
                End If
            End If

        Next
        'Return rgbPixels   ' test only
    End Sub           ' test Function; change to sub for real experiment.

    Private Function ConvertGray16ToRGB(ByVal grayPixels() As Int16, ByVal offset As Integer, ByVal samples As Integer, ByRef processedGrayPixels() As Integer) As Integer()
        Dim rgbPixels((samples \ m_iSamplesPerPixel) - 1) As Integer
        'Dim ProcessedGrayPixels((samples \ m_iSamplesPerPixel) - 1) As Integer
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

            ''could subtract 8192 from averaged samples. but doesnt give same values
            'ProcessedGrayPixels(i) = CInt(iSum / iCount) - 8192
            'If ProcessedGrayPixels(i) < 0 Then
            '    ProcessedGrayPixels(i) = 0
            'End If

            'get 8bit values for rgb bmp image (shift bits)
            Dim forRGB As Integer = processedGrayPixels(i) >> m_iBitsToShift
            ' put the values into r,g and b channels
            'rgbPixels(i) = CInt(forRGB << 16) Or CInt(forRGB << 8) Or CInt(forRGB)
            rgbPixels(i) = forRGB Or (forRGB << 8) Or (forRGB << 16)
        Next



        '---- for test only ---
		'update some GUI labels
        'Dim avg_pixel_val As Double = ProcessedGrayPixels.Average()
        'Dim max_pixel_val As Double = ProcessedGrayPixels.Max()
        'Dim min_pixel_val As Double = ProcessedGrayPixels.Min()
        'avg_val_label.Text = Format(avg_pixel_val, "0")
        'max_val_label.Text = Format(max_pixel_val, "0")
        'min_val_label.Text = Format(min_pixel_val, "0")

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
		'--- end test only ---

        Return rgbPixels
    End Function

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
        Else
            Chart2.Series(ROIIdx).Points.DataBindY(ThresholdLineData)  ' +4 because 4 graphs are hardcoded...
        End If

    End Sub



    Private Sub DeliverTriggers(ByVal ROIIdx As Integer)
        If AllResetFrames > 0 Then
            AllTriggersEnabled = False
        End If

        '' ---------     Trigger by number of pulses   ------------------
        'Dim AdvanceSLMBy As Integer = ROIIdx + 1 - CurrentSLMPattern
        'If AdvanceSLMBy < 0 Then
        '    AdvanceSLMBy = NumSLMpatterns + AdvanceSLMBy
        'End If
        'For i As Integer = 0 To OutputArrayLength
        '    OutputArray(0, i) = TriggersToSLM(AdvanceSLMBy, i)
        '    OutputArray(1, i) = TriggerToPV(AdvanceSLMBy, i)
        'Next
        'writer.BeginWriteMultiSample(True, OutputArray, Nothing, Nothing)
        '' --------------------------------------------------------------


        '' ---------     Trigger by analog output   ---------------------
        'For i As Integer = 0 To 10
        '    AnalogOutputArray(0, i) = AnalogLUT(AllTriggerStatus)
        'Next

        'writer.BeginWriteMultiSample(True, AnalogOutputArray, Nothing, Nothing)
        ''writerD.BeginWriteMultiSamplePort(True, DigitalOutputArray, Nothing, Nothing)
        ''----------------------------------------------------------------

        ''----------    Trigger by TCP to Zoo   --------------------------
        If TrialStatus(NumFrames - 1) = True Or IsSensoryStim = True Then
            Dim vTimeout As Integer = 50  'ms 
            Dim sendbytes() As Byte = System.Text.Encoding.ASCII.GetBytes(AllTriggerStatus.ToString(fmt))
            'writer.BeginWriteMultiSample(True, ToSLMOutputArray, Nothing, Nothing)  ' for test only
            TCPClients.Client.Send(sendbytes)
            TCPClients.ReceiveTimeout = vTimeout
            sendbytes = New [Byte](1) {}
            Dim bytes As Int32 = TCPClientStream.Read(sendbytes, 0, sendbytes.Length)
            Dim echoData = System.Text.Encoding.ASCII.GetString(sendbytes, 0, bytes)

            writerS2.BeginWriteMultiSample(True, ToPVtrigger, Nothing, Nothing) 'write to channel a01

            StatusText.Text = "Pattern " & echoData & " stimulated"
            StimNum += 1


            RecordArray(2, StimNum) = echoData
        Else
            StatusText.Text = "Pattern " & CStr(AllTriggerStatus) & " triggered"
            ' LastTriThresh(ROIIdx) = ROIThreshold(ROIIdx)

        End If
        SLMPatternLabel.Text = CStr(AllTriggerStatus)
        
        '-------------------End  Trigger by TCP to Zoo  -----------------------------------------------


        ''----------    Trigger by TCP to PV     --------------------------
        'm_pl.SendScriptCommands("-mp")
        'm_pl.SendScriptCommands("-mp .5 .5 10 Fidelity 250 true .03 3 ")
        'm_pl_s.SendScriptCommands("-mp .7 .7 30 Fidelity 250 true .015 3 ")
        '--------------------------------------------------------------------

        '--- simple trigger   only send spirals during experiment

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

        '--- end simple trigger ---

 
        lblStimNum.Text = CStr(StimNum)
		' record stim frame and pattern
        RecordArray(0, StimNum) = CStr(AllTriggerStatus)
        RecordArray(1, StimNum) = CStr(NumFrames)
        'RecordArray(3, StimNum) = CStr(echoData)
        'RecordArray(4, StimNum) = CStr(ROIBaseline(0))

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
        AllTriggerStatus = 0    ' zero pattern No.
        lblTriggerInterval.Text = CStr(m_oTimer.ElapsedMilliseconds - LastTrigger)
        LastTrigger = m_oTimer.ElapsedMilliseconds
        LastTriFrame = NumFrames
        'Dff_StimFrames(ROIIdx) = NumFrames - LastStimFrame(ROIIdx)


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



    Private Sub SaveButton_clicked(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button3.Click
        ' SAVE
        Dim DataArray As String(,) = RecordArray
        Dim FileName As String
        If TboxFilename.Text.Trim.Length > 0 Then
            FileName &= filepath & "\" & TboxFilename.Text & "_" & TboxAqNum.Text & ".txt"
        Else
            FileName &= filepath & "\output_" & DateTime.Now.ToString("yyyyMMdd-HHmmss") & ".txt"
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
    End Sub

    Private Sub ButtonNewRecording_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonNewRecording.Click
        writerS1.BeginWriteMultiSample(True, ClampOffArray, Nothing, Nothing) 'write to channel a00
        writerS2.BeginWriteMultiSample(True, ToPVreset, Nothing, Nothing) 'write to channel a01
        NumExperiment = CInt(TboxAqNum.Text) + 1
        TboxAqNum.Text = CStr(NumExperiment)
        ReDim RecordArray(2, 12000)
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
        ' redim false alarm detection
        'Num_FA = 0
        'ReDim Noise_floor(NumTriggerROIs - 1)
        'ReDim Num_Sus(NumTriggerROIs - 1)
        'For i As Integer = 0 To NumTriggerROIs - 1
        '    Dff_StimFrames(i) = 500
        'Next

        'for sensory stim

        SenStimDetected = False
        NumFramesPostStim = 0
        NumSenStim = 0

        'for low-pass=filter
        ReDim NumPostStim(NumTriggerROIs - 1)
        ReDim LastTriThresh(NumTriggerROIs - 1)
        ReDim TriggerOnFlag(NumTriggerROIs - 1)
        ReDim TriggerOnFrame(NumTriggerROIs - 1)
        For i As Integer = 0 To NumTriggerROIs - 1
            TriggerEnabled(i) = True
        Next

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



    Private Sub ButtonBrowse_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles ButtonBrowse.Click
        Using dialogBrowse As New FolderBrowserDialog
            If dialogBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                TextBoxFilepath.Text = dialogBrowse.SelectedPath
                filepath = dialogBrowse.SelectedPath
            End If
        End Using
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
        Else

            StartRecordingButton.Text = "Start recording"
            StartRecordingButton.BackColor = Color.Gray
            NumBaselineSpRequired = 150
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
        NumFramesPosttimRequired = CInt(numWaitingFrames.Value)
    End Sub

    Private Sub BtnConnect_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles BtnConnect.Click
        Try
            TCPClients = New Sockets.TcpClient("128.40.156.163", 8888)
            TCPClientStream = TCPClients.GetStream()
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
        ReDim TrialStatus(30000 - 1)
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


    Private Sub LFPcheckbox_CheckedChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles LFPcheckbox.CheckedChanged
        EnableLPF = Not EnableLPF
    End Sub

    Private Sub NumericUpDown_controlframes_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDown_controlframes.ValueChanged
        NumControlFrames = CInt(NumericUpDown_controlframes.Value)
        ''-- for trigger-target
        ReDim TrialStatus(30000 - 1)
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
            ReDim TrialStatus(30000 - 1)
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
            ReDim TrialStatus(30000 - 1)
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

    Private Sub NumericUpDownSensoryControlFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownSensoryControlFrames.ValueChanged
        NumSensoryControlFrames = CInt(NumericUpDownSensoryControlFrames.Value)
    End Sub

    Private Sub NumericUpDownNumStimFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumStimFrames.ValueChanged
        NumStimFrames = CInt(NumericUpDownNumStimFrames.Value)
    End Sub

    Private Sub NumericUpDownNumStims_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumStims.ValueChanged
        TotalSensoryStims = CInt(NumericUpDownNumStims.Value)
    End Sub

    Private Sub BtnSensoryConnect_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles BtnSensoryConnect.Click
        Try
            TCPClients2 = New Sockets.TcpClient("128.40.156.163", 8070)
            TCPClientStream2 = TCPClients2.GetStream()
        Catch ex As Exception
            MsgBox("Cannot connect to server")
        End Try

    End Sub

    Private Sub NumericUpDownStimType_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownStimType.ValueChanged
        SensoryStimType = NumericUpDownStimType.Value
        sendbytes2 = System.Text.Encoding.ASCII.GetBytes(SensoryStimType)
    End Sub

    Private Sub NumericUpDownNumWaitingFrames_ValueChanged(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles NumericUpDownNumWaitingFrames.ValueChanged
        NumWaitContinuousFrames = CInt(NumericUpDownNumWaitingFrames.Value)
    End Sub
End Class