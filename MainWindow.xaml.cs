using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using MapControl;
using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GPSReaderApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ConfigureMap();  // 지도 초기 설정
            LoadAvailablePorts();  // 사용 가능한 COM 포트 로드
        }
        private SerialPort serialPort;
        private bool isReading = false;
        private string buffer = "";

        

        private void ConfigureMap()
        {
            // GMaps 인스턴스 초기화 (서버와 캐시 접근 모드 설정)
            GMaps.Instance.Mode = AccessMode.ServerAndCache;
            Map.MapProvider = GMapProviders.OpenStreetMap;  // 지도 제공자 설정
            Map.Position = new PointLatLng(37.5665, 126.9780);  // 기본 위치: 서울
            Map.MinZoom = 2;  // 최소 줌 레벨
            Map.MaxZoom = 17;  // 최대 줌 레벨
            Map.Zoom = 15;  // 초기 줌 레벨
            Map.ShowCenter = false;  // 중앙 점 표시 비활성화
        }

        private void LoadAvailablePorts()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                ComPortComboBox.Items.Add(port);
            }

            if (ComPortComboBox.Items.Count > 0)
            {
                ComPortComboBox.SelectedIndex = 0;
            }
            else
            {
                GpsDataListBox.Items.Add("사용 가능한 COM 포트가 없습니다.");
            }
        }

        private void StartReading_Click(object sender, RoutedEventArgs e)
        {
            if (!isReading)
            {
                if (ComPortComboBox.SelectedItem == null || BaudRateComboBox.SelectedItem == null)
                {
                    GpsDataListBox.Items.Add("COM 포트와 보레이트를 선택하세요.");
                    return;
                }

                string selectedPort = ComPortComboBox.SelectedItem.ToString();
                int selectedBaudRate = int.Parse(((ComboBoxItem)BaudRateComboBox.SelectedItem).Content.ToString());

                try
                {
                    serialPort = new SerialPort(selectedPort, selectedBaudRate);
                    serialPort.DataReceived += SerialPort_DataReceived;
                    serialPort.Open();
                    isReading = true;
                    GpsDataListBox.Items.Add($"GPS 데이터 수신을 시작합니다... (포트: {selectedPort}, 보레이트: {selectedBaudRate})");
                }
                catch (Exception ex)
                {
                    GpsDataListBox.Items.Add($"오류: {ex.Message}");
                }
            }
        }

        private void StopReading_Click(object sender, RoutedEventArgs e)
        {
            if (isReading && serialPort.IsOpen)
            {
                serialPort.Close();
                isReading = false;
                GpsDataListBox.Items.Add("GPS 데이터 수신을 중지합니다.");
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            string data = sp.ReadExisting();
            buffer += data;

            int newlineIndex;
            while ((newlineIndex = buffer.IndexOf('\n')) != -1)
            {
                string completeSentence = buffer.Substring(0, newlineIndex).Trim();
                if (!string.IsNullOrEmpty(completeSentence))
                {
                    string formattedData = FormatGpsData(completeSentence);
                    Dispatcher.Invoke(() =>
                    {
                        GpsDataListBox.Items.Add(formattedData);
                    });
                }
                buffer = buffer.Substring(newlineIndex + 1);
            }
        }

        private string FormatGpsData(string sentence)
        {
            sentence = sentence.Trim();

            if (sentence.StartsWith("$GPGGA") || sentence.StartsWith("$GNGGA"))
            {
                return ParseGpggaSentence(sentence);
            }
            else if (sentence.StartsWith("$GPRMC") || sentence.StartsWith("$GNRMC"))
            {
                return ParseGprmcSentence(sentence);
            }
            else
            {
                return $"지원되지 않는 문장 유형: {sentence}";
            }
        }

        private string ParseGpggaSentence(string sentence)
        {
            string[] parts = sentence.Split(',');
            if (parts.Length >= 14)
            {
                string latitudeStr = ConvertDmsToDecimal(parts[2], parts[3]);
                string longitudeStr = ConvertDmsToDecimal(parts[4], parts[5]);

                // 유효성 검사 추가: "N/A"일 경우 기본값을 반환
                if (latitudeStr == "N/A" || longitudeStr == "N/A")
                {
                    Dispatcher.Invoke(() =>
                    {
                        GpsSignalStatusTextBlock.Text = "GPS 신호 없음";  // 신호 상태 업데이트
                        GpsSignalStatusTextBlock.Foreground = Brushes.Red;  // 신호 없음일 때 빨간색
                        GpsDataListBox.Items.Add($"유효하지 않은 위치 데이터입니다.");
                    });
                    return $"{DateTime.Now:yyyy년 MM월 dd일 HH시 mm분 ss초} UTC에 위치는 위도 {latitudeStr}, 경도 {longitudeStr}에서 고도는 {parts[9]} 미터입니다.";
                }

                try
                {
                    // 지도에 표시하기 위한 원본 데이터
                    double latitude = Convert.ToDouble(latitudeStr);
                    double longitude = Convert.ToDouble(longitudeStr);
                    string time = ParseTime(parts[1]);
                    string altitude = $"{parts[9]} 미터";

                    // 신호 상태가 유효할 경우 업데이트
                    Dispatcher.Invoke(() =>
                    {
                        GpsSignalStatusTextBlock.Text = "GPS 신호 상태: 유효";
                        GpsSignalStatusTextBlock.Foreground = Brushes.Green;  // 유효한 신호일 때 초록색
                    });

                    // 자연어로 변환된 데이터 출력 (리스트박스)
                    Dispatcher.Invoke(() =>
                    {
                        GpsDataListBox.Items.Add($"{DateTime.Now:yyyy년 MM월 dd일 HH시 mm분 ss초} UTC에 위치는 위도 {latitudeStr}, 경도 {longitudeStr}에서 고도는 {altitude}입니다.");
                    });

                    // 지도 위치 업데이트 (원본 데이터 사용)
                    Dispatcher.Invoke(() => UpdateMapLocation(latitude, longitude));

                    return $"{time}에 위도 {latitude}, 경도 {longitude}에서 고도는 {altitude}입니다.";
                }
                catch (FormatException ex)
                {
                    // 변환 실패 시 예외 처리
                    Dispatcher.Invoke(() =>
                    {
                        GpsDataListBox.Items.Add($"데이터 변환 오류: {ex.Message}");
                    });
                    return "유효하지 않은 위치 데이터입니다.";
                }
            }

            Dispatcher.Invoke(() =>
            {
                GpsSignalStatusTextBlock.Text = "GPS 신호 없음";  // 신호 상태 업데이트
                GpsSignalStatusTextBlock.Foreground = Brushes.Red;  // 신호 없음일 때 빨간색
            });

            return "유효하지 않은 GPGGA 문장입니다.";
        }

        private string ParseGprmcSentence(string sentence)
        {
            string[] parts = sentence.Split(',');
            if (parts.Length >= 12)
            {
                string latitudeStr = ConvertDmsToDecimal(parts[3], parts[4]);
                string longitudeStr = ConvertDmsToDecimal(parts[5], parts[6]);

                // 유효성 검사 추가: "N/A"일 경우 기본값을 반환
                if (latitudeStr == "N/A" || longitudeStr == "N/A")
                {
                    Dispatcher.Invoke(() =>
                    {
                        GpsSignalStatusTextBlock.Text = "GPS 신호 없음";  // 신호 상태 업데이트
                        GpsSignalStatusTextBlock.Foreground = Brushes.Red;  // 신호 없음일 때 빨간색
                        GpsDataListBox.Items.Add($"유효하지 않은 위치 데이터입니다.");
                    });
                    return $"{DateTime.Now:HH시 mm분 ss초} UTC에 위치는 위도 {latitudeStr}, 경도 {longitudeStr}에서 고도는 {parts[9]} 미터입니다.";
                }

                try
                {
                    // 지도에 표시하기 위한 원본 데이터
                    double latitude = Convert.ToDouble(latitudeStr);
                    double longitude = Convert.ToDouble(longitudeStr);
                    string time = ParseTime(parts[1]);
                    string status = parts[2] == "A" ? "유효" : "유효하지 않음";
                    string speed = $"{ConvertKnotsToKmph(double.Parse(parts[7]))} km/h";
                    string date = ParseDate(parts[9]);

                    // 신호 상태가 유효할 경우 업데이트
                    Dispatcher.Invoke(() =>
                    {
                        GpsSignalStatusTextBlock.Text = "GPS 신호 상태: 유효";
                        GpsSignalStatusTextBlock.Foreground = Brushes.Green;  // 유효한 신호일 때 초록색
                    });

                    // 자연어로 변환된 데이터 출력 (리스트박스)
                    Dispatcher.Invoke(() =>
                    {
                        GpsDataListBox.Items.Add($"{date} {time} UTC에 위치는 위도 {latitudeStr}, 경도 {longitudeStr}에서 고도는 {parts[9]} 미터입니다.");
                    });

                    // 지도 위치 업데이트 (원본 데이터 사용)
                    Dispatcher.Invoke(() => UpdateMapLocation(latitude, longitude));

                    return $"{date} {time}에 위치는 위도 {latitude}, 경도 {longitude}이며, 속도는 {speed}입니다. GPS 신호 상태: {status}";
                }
                catch (FormatException ex)
                {
                    // 변환 실패 시 예외 처리
                    Dispatcher.Invoke(() =>
                    {
                        GpsDataListBox.Items.Add($"데이터 변환 오류: {ex.Message}");
                    });
                    return "유효하지 않은 위치 데이터입니다.";
                }
            }

            Dispatcher.Invoke(() =>
            {
                GpsSignalStatusTextBlock.Text = "GPS 신호 없음";  // 신호 상태 업데이트
                GpsSignalStatusTextBlock.Foreground = Brushes.Red;  // 신호 없음일 때 빨간색
            });

            return "유효하지 않은 GPRMC 문장입니다.";
        }

        private void UpdateMapLocation(double latitude, double longitude)
        {
            Map.Position = new PointLatLng(latitude, longitude);

            // 기존 마커 제거
            Map.Markers.Clear();

            // 새로운 마커 추가
            GMapMarker marker = new GMapMarker(new PointLatLng(latitude, longitude))
            {
                Shape = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2
                }
            };
            Map.Markers.Add(marker);
        }

        private string ParseTime(string time)
        {
            if (time.Length >= 6)
            {
                int hours = int.Parse(time.Substring(0, 2));
                int minutes = int.Parse(time.Substring(2, 2));
                int seconds = int.Parse(time.Substring(4, 2));

                DateTime utcTime = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, hours, minutes, seconds, DateTimeKind.Utc);
                DateTime kstTime = utcTime.AddHours(9); // KST로 변환

                return $"{kstTime.Hour}시 {kstTime.Minute}분 {kstTime.Second}초 KST";
            }
            return "알 수 없는 시간";
        }

        private string ParseDate(string date)
        {
            if (date.Length >= 6)
            {
                string day = date.Substring(0, 2);
                string month = date.Substring(2, 2);
                string year = $"20{date.Substring(4, 2)}";
                return $"{year}년 {month}월 {day}일";
            }
            return "알 수 없는 날짜";
        }

        private string ConvertDmsToDecimal(string dms, string direction)
        {
            if (string.IsNullOrEmpty(dms) || dms.Length < 4)
                return "N/A";

            try
            {
                double degrees = double.Parse(dms.Substring(0, 2));
                double minutes = double.Parse(dms.Substring(2)) / 60;
                double decimalDegrees = degrees + minutes;

                if (direction == "S" || direction == "W")
                    decimalDegrees *= -1;

                return decimalDegrees.ToString("0.000000°");
            }
            catch (FormatException)
            {
                return "N/A";  // 유효하지 않은 경우 "N/A" 반환
            }
        }

        private string ConvertKnotsToKmph(double knots)
        {
            return (knots * 1.852).ToString("0.00");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (isReading && serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }
}
