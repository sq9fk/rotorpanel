namespace RotorPanel;

/// <summary>Sprawdzanie i instalowanie aktualizacji.</summary>
public partial class MainForm
{
    private bool _sprawdzanieWersji;

    /// <summary>Sprawdzenie w tle przy starcie - bez komunikatu, gdy nic nowego nie ma.</summary>
    private async void SprawdzAktualizacjeWTle()
    {
        // Chwila zwloki, zeby nie rywalizowac z zestawianiem mostkow przy starcie.
        await Task.Delay(4000);

        // Poprzednia kopia programu konczy prace juz po starcie nowej, wiec przy
        // samym uruchomieniu plik .old bywa jeszcze zajety.
        Aktualizacja.PosprzatajPoAktualizacji();

        if (!_cfg.SprawdzajAktualizacje) return;
        await SprawdzAktualizacje(recznie: false);
    }

    private async Task SprawdzAktualizacje(bool recznie)
    {
        if (_sprawdzanieWersji) return;
        _sprawdzanieWersji = true;

        try
        {
            Wydanie wydanie = null;
            try { wydanie = await Aktualizacja.SprawdzNowsze(); }
            catch (Exception ex)
            {
                if (recznie)
                    MessageBox.Show(this,
                        "Nie udało się sprawdzić aktualizacji:" + Environment.NewLine + ex.Message,
                        "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsDisposed) return;

            if (wydanie == null)
            {
                if (recznie)
                    MessageBox.Show(this,
                        "Masz najnowszą wersję (" + Aktualizacja.WersjaBiezaca.ToString(3) + ").",
                        "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Pytanie znikad, gdy program siedzi w zasobniku, jest mylace - najpierw panel.
            PokazOkno();
            var odp = MessageBox.Show(this,
                "Dostępna jest wersja " + wydanie.Wersja.ToString(3) +
                ", masz " + Aktualizacja.WersjaBiezaca.ToString(3) + "." +
                Environment.NewLine + Environment.NewLine +
                "Pobrać ją i uruchomić program ponownie?",
                "Aktualizacja RotorPanel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (odp != DialogResult.Yes) return;

            await Zainstaluj(wydanie);
        }
        finally { _sprawdzanieWersji = false; }
    }

    private async Task Zainstaluj(Wydanie wydanie)
    {
        string pobrany;
        try
        {
            pobrany = await Aktualizacja.Pobierz(wydanie);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nie udało się pobrać aktualizacji:" + Environment.NewLine + ex.Message,
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Mostki trzymaja porty, a za chwile startuje nowa kopia programu.
        foreach (var m in _mostki) m.Stop();

        try
        {
            Aktualizacja.Podmien(pobrany);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Nie udało się podmienić pliku programu:" + Environment.NewLine + ex.Message +
                Environment.NewLine + Environment.NewLine +
                "Jeśli program leży w katalogu wymagającym uprawnień, przenieś go w inne miejsce " +
                "albo pobierz nową wersję ręcznie.",
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            // Blokada musi puscic, zanim wystartuje nowa kopia - inaczej uzna sie
            // za druga instancje i tylko obudzi te konczaca prace.
            Program.ZwolnijBlokade();
            System.Diagnostics.Process.Start(Aktualizacja.SciezkaProgramu,
                                             Program.ArgumentPoAktualizacji);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Program zaktualizowano, ale nie udało się go uruchomić ponownie:" +
                Environment.NewLine + ex.Message,
                "RotorPanel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        ZamknijNaprawde();
    }
}
