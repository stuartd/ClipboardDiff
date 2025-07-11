using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ClipboardDiff {
	class Program {
		static void Main(string[] args) {
			string text1 = null;
			string text2 = null;

			const string sql = "select ooData from data where strClipBoardFormat = 'CF_TEXT' and LParentId in (select lId from main order by lID desc limit 2)";
			var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ditto", "Ditto.db;Version=3;");

            using (var connection = new SQLiteConnection($"Data Source={path}")) {
				using (var command = new SQLiteCommand(sql, connection)) {
					command.Prepare();
					connection.Open();
					using (var reader = command.ExecuteReader()) {

						string GetValue() {
							var bytes = (byte[]) reader.GetValue(0);
							return Encoding.UTF8.GetString(bytes.Where(b => b != 0).ToArray());
						}

						if (reader.Read()) {
							text1 = GetValue();
						}

						if (reader.Read()) {
							text2 = GetValue();
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(text1) && !string.IsNullOrEmpty(text2)) {
				var tempFile1 = Path.GetTempFileName();
				File.WriteAllText(tempFile1, text1);

				var tempFile2 = Path.GetTempFileName();
				File.WriteAllText(tempFile2, text2);

                var psi = new ProcessStartInfo
                {
                    FileName = @"C:\Program Files\SourceGear\Common\DiffMerge\sgdm.exe"
                };
                psi.ArgumentList.Add("/title1=Older");
                psi.ArgumentList.Add("/title2=Newer");
                psi.ArgumentList.Add(tempFile1);
                psi.ArgumentList.Add(tempFile2);
                psi.UseShellExecute = true;

                Process.Start(psi);
			}
		}
	}
}
