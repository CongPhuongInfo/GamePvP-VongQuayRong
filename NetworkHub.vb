Option Strict On
Option Explicit On

Imports System.Net
Imports System.Net.Sockets
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Phia Host quan ly nhieu ket noi TCP (toi da 3 Khach, cong Host la du 4 nguoi choi).
''' Khach (Client) van dung NetworkPeer.vb (khong doi) de ConnectToHost nhu cu.
''' Host dung lop nay de Accept nhieu client, gan SeatIndex (1,2,3) va Broadcast STATE.
''' Tat ca event duoc marshal ve UI thread qua uiControl.Invoke.
''' </summary>
Public Class NetworkHub

    Public Const MAX_CLIENTS As Integer = 3 ' seat 1,2,3 ; seat 0 la Host

    Public Event ClientConnected(seatIndex As Integer)
    Public Event ClientDisconnected(seatIndex As Integer)
    Public Event LineReceivedFromClient(seatIndex As Integer, line As String)

    Private listener As TcpListener
    Private uiControl As Control
    Private isRunning As Boolean = False

    Private slotClient(MAX_CLIENTS) As TcpClient      ' index 1..3 duoc dung
    Private slotWriter(MAX_CLIENTS) As StreamWriter
    Private slotReader(MAX_CLIENTS) As StreamReader
    Private slotUsed(MAX_CLIENTS) As Boolean

    Public Sub New(uiOwner As Control)
        uiControl = uiOwner
    End Sub

    Public Sub StartListening(port As Integer)
        listener = New TcpListener(IPAddress.Any, port)
        listener.Start()
        isRunning = True
        Dim acceptThread As New Thread(New ThreadStart(AddressOf AcceptLoop))
        acceptThread.IsBackground = True
        acceptThread.Start()
    End Sub

    Private Sub AcceptLoop()
        Do While isRunning
            Try
                Dim incoming As TcpClient = listener.AcceptTcpClient()
                Dim seat As Integer = FindFreeSeat()
                If seat = -1 Then
                    ' Da du nguoi, dong ket noi moi.
                    Try
                        incoming.Close()
                    Catch
                    End Try
                Else
                    slotClient(seat) = incoming
                    slotUsed(seat) = True
                    Dim stream As NetworkStream = incoming.GetStream()
                    slotReader(seat) = New StreamReader(stream, Encoding.UTF8)
                    slotWriter(seat) = New StreamWriter(stream, Encoding.UTF8)
                    slotWriter(seat).AutoFlush = True

                    RaiseClientConnected(seat)

                    Dim readThread As New Thread(New ParameterizedThreadStart(AddressOf ClientReadLoop))
                    readThread.IsBackground = True
                    readThread.Start(seat)
                End If
            Catch ex As Exception
                If isRunning Then
                    ' Listener bi dong hoac loi, thoat loop.
                End If
                Exit Do
            End Try
        Loop
    End Sub

    Private Function FindFreeSeat() As Integer
        Dim i As Integer
        For i = 1 To MAX_CLIENTS
            If Not slotUsed(i) Then Return i
        Next i
        Return -1
    End Function

    Private Sub ClientReadLoop(state As Object)
        Dim seat As Integer = CType(state, Integer)
        Try
            Do While isRunning AndAlso slotUsed(seat)
                Dim line As String = slotReader(seat).ReadLine()
                If line Is Nothing Then Exit Do
                RaiseLineReceived(seat, line)
            Loop
        Catch ex As Exception
            ' Mat ket noi, xu ly Disconnected ben duoi.
        End Try
        slotUsed(seat) = False
        RaiseClientDisconnected(seat)
    End Sub

    Public Sub SendToClient(seatIndex As Integer, line As String)
        If seatIndex < 1 OrElse seatIndex > MAX_CLIENTS Then Return
        If Not slotUsed(seatIndex) Then Return
        Try
            slotWriter(seatIndex).WriteLine(line)
        Catch ex As Exception
            slotUsed(seatIndex) = False
            RaiseClientDisconnected(seatIndex)
        End Try
    End Sub

    Public Sub Broadcast(line As String)
        Dim i As Integer
        For i = 1 To MAX_CLIENTS
            If slotUsed(i) Then SendToClient(i, line)
        Next i
    End Sub

    ''' <summary>Gui cho tat ca Client dang ket noi, tru seat chi dinh (dung khi relay
    ''' lai 1 tin nhan chat ma chinh Client do vua gui len, tranh hien thi trung lap).</summary>
    Public Sub BroadcastExcept(line As String, exceptSeat As Integer)
        Dim i As Integer
        For i = 1 To MAX_CLIENTS
            If i <> exceptSeat AndAlso slotUsed(i) Then SendToClient(i, line)
        Next i
    End Sub

    Public Function ConnectedCount() As Integer
        Dim c As Integer = 0
        Dim i As Integer
        For i = 1 To MAX_CLIENTS
            If slotUsed(i) Then c += 1
        Next i
        Return c
    End Function

    Public Function IsSeatUsed(seatIndex As Integer) As Boolean
        If seatIndex < 1 OrElse seatIndex > MAX_CLIENTS Then Return False
        Return slotUsed(seatIndex)
    End Function

    Public Sub CloseAll()
        isRunning = False
        Dim i As Integer
        For i = 1 To MAX_CLIENTS
            Try
                If slotClient(i) IsNot Nothing Then slotClient(i).Close()
            Catch
            End Try
            slotUsed(i) = False
        Next i
        Try
            If listener IsNot Nothing Then listener.Stop()
        Catch
        End Try
    End Sub

    Private Sub RaiseClientConnected(seat As Integer)
        If uiControl.InvokeRequired Then
            uiControl.Invoke(CType(AddressOf DoRaiseConnected, Action(Of Integer)), seat)
        Else
            DoRaiseConnected(seat)
        End If
    End Sub
    Private Sub DoRaiseConnected(seat As Integer)
        RaiseEvent ClientConnected(seat)
    End Sub

    Private Sub RaiseClientDisconnected(seat As Integer)
        If uiControl.InvokeRequired Then
            uiControl.Invoke(CType(AddressOf DoRaiseDisconnected, Action(Of Integer)), seat)
        Else
            DoRaiseDisconnected(seat)
        End If
    End Sub
    Private Sub DoRaiseDisconnected(seat As Integer)
        RaiseEvent ClientDisconnected(seat)
    End Sub

    Private Sub RaiseLineReceived(seat As Integer, line As String)
        If uiControl.InvokeRequired Then
            uiControl.Invoke(CType(AddressOf DoRaiseLine, Action(Of Integer, String)), seat, line)
        Else
            DoRaiseLine(seat, line)
        End If
    End Sub
    Private Sub DoRaiseLine(seat As Integer, line As String)
        RaiseEvent LineReceivedFromClient(seat, line)
    End Sub

End Class
