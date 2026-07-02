Option Strict On
Option Explicit On

Imports System.Net
Imports System.Net.Sockets
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Ket noi TCP don gian giua 2 nguoi choi (Host = server, Client = nguoi tham gia).
''' Giao thuc la text, moi dong ket thuc bang newline. Tat ca event duoc marshal
''' ve UI thread thong qua uiControl.Invoke de an toan khi cap nhat Form.
''' </summary>
Public Class NetworkPeer

    Public Event LineReceived(line As String)
    Public Event Disconnected()
    Public Event Connected()

    Private peerClient As TcpClient
    Private peerListener As TcpListener
    Private peerStream As NetworkStream
    Private peerReader As StreamReader
    Private peerWriter As StreamWriter
    Private readThread As Thread
    Private uiControl As Control
    Private isRunning As Boolean = False

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return peerClient IsNot Nothing AndAlso peerClient.Connected
        End Get
    End Property

    Public Sub New(uiOwner As Control)
        uiControl = uiOwner
    End Sub

    ''' <summary>Bat dau lang nghe va cho 1 client ket noi (chay tren thread rieng).</summary>
    Public Sub StartHost(port As Integer)
        peerListener = New TcpListener(IPAddress.Any, port)
        peerListener.Start()
        Dim acceptThread As New Thread(New ThreadStart(AddressOf AcceptLoop))
        acceptThread.IsBackground = True
        acceptThread.Start()
    End Sub

    Private Sub AcceptLoop()
        Try
            peerClient = peerListener.AcceptTcpClient()
            SetupStreamsAndStart()
        Catch ex As Exception
            RaiseDisconnected()
        End Try
    End Sub

    ''' <summary>Ket noi den host (chay tren thread rieng).</summary>
    Public Sub ConnectToHost(ipAddress As String, port As Integer)
        Dim connectThread As New Thread(New ParameterizedThreadStart(AddressOf ConnectLoop))
        connectThread.IsBackground = True
        connectThread.Start(New String() {ipAddress, port.ToString()})
    End Sub

    Private Sub ConnectLoop(state As Object)
        Try
            Dim args As String() = DirectCast(state, String())
            peerClient = New TcpClient()
            peerClient.Connect(args(0), Integer.Parse(args(1)))
            SetupStreamsAndStart()
        Catch ex As Exception
            RaiseDisconnected()
        End Try
    End Sub

    Private Sub SetupStreamsAndStart()
        peerStream = peerClient.GetStream()
        peerReader = New StreamReader(peerStream, Encoding.UTF8)
        peerWriter = New StreamWriter(peerStream, Encoding.UTF8)
        peerWriter.AutoFlush = True
        isRunning = True

        RaiseConnected()

        readThread = New Thread(New ThreadStart(AddressOf ReadLoop))
        readThread.IsBackground = True
        readThread.Start()
    End Sub

    Private Sub ReadLoop()
        Try
            Do While isRunning
                Dim line As String = peerReader.ReadLine()
                If line Is Nothing Then
                    Exit Do
                End If
                RaiseLineReceived(line)
            Loop
        Catch ex As Exception
            ' Mat ket noi hoac loi doc stream, se bao qua Disconnected ben duoi.
        End Try
        RaiseDisconnected()
    End Sub

    Public Sub SendLine(line As String)
        Try
            If peerWriter IsNot Nothing Then
                peerWriter.WriteLine(line)
            End If
        Catch ex As Exception
            RaiseDisconnected()
        End Try
    End Sub

    Public Sub CloseConnection()
        isRunning = False
        Try
            If peerClient IsNot Nothing Then peerClient.Close()
        Catch ex As Exception
        End Try
        Try
            If peerListener IsNot Nothing Then peerListener.Stop()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub RaiseLineReceived(line As String)
        If uiControl.InvokeRequired Then
            uiControl.Invoke(CType(AddressOf DoRaiseLine, Action(Of String)), line)
        Else
            DoRaiseLine(line)
        End If
    End Sub

    Private Sub DoRaiseLine(line As String)
        RaiseEvent LineReceived(line)
    End Sub

    Private Sub RaiseDisconnected()
        isRunning = False
        If uiControl.InvokeRequired Then
            uiControl.Invoke(New Action(AddressOf DoRaiseDisconnected))
        Else
            DoRaiseDisconnected()
        End If
    End Sub

    Private Sub DoRaiseDisconnected()
        RaiseEvent Disconnected()
    End Sub

    Private Sub RaiseConnected()
        If uiControl.InvokeRequired Then
            uiControl.Invoke(New Action(AddressOf DoRaiseConnected))
        Else
            DoRaiseConnected()
        End If
    End Sub

    Private Sub DoRaiseConnected()
        RaiseEvent Connected()
    End Sub

End Class
