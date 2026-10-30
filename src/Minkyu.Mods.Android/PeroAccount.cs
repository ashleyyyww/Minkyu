using Il2Cpp;
using Il2CppUI.PnlAccount;
using Il2CppAccount;
using Il2CppAssets.Scripts.UI.Panels.PnlAccount;
using Il2CppAssets.Scripts.UI.Tips;
using UnityEngine;
using UnityEngine.UI;

namespace Minkyu.Mods;

internal static class PeroAccount
{
    internal static bool IsLoggedIn => GameAccountSystem.instance?.IsLoggedIn() == true;

    internal static void ChangeName()
    {
        if (!IsLoggedIn)
        {
            Open();
            return;
        }
        var form = PnlTipsManager.instance?.GetMessageBox("PnlIdInput")?.TryCast<PnlIdInput>();
        if (form == null) throw new InvalidOperationException("Game name editor is unavailable");
        form.Show();
    }

    internal static void Open()
    {
        var account = AccountCanvas.instance;
        if (account == null) throw new InvalidOperationException("Game account menu is unavailable");
        account.OpenPeroLogin();
        var login = account.m_PeroLoginV2;
        if (login == null) throw new InvalidOperationException("Game email login form is unavailable");
        login.m_MainLoginGroup.SetActive(true);
        login.m_OverseaLoginGroup.SetActive(false);
        login.m_InputAccountStr.contentType = InputField.ContentType.EmailAddress;
        login.m_InputAccountStr.keyboardType = TouchScreenKeyboardType.EmailAddress;
        var address = login.m_InputAccountStr.placeholder?.TryCast<Text>();
        if (address != null) address.text = "Email address";
        var code = login.m_InputCode.placeholder?.TryCast<Text>();
        if (code != null) code.text = "Verification code";
        Label(login.m_BtnSendCode, "Send code");
        Label(login.m_BtnPeroLogin, "Sign in / Register");
        foreach (var button in new[] { login.m_BtnXdLogin, login.m_BtnGoogleLogin, login.m_BtnAppleLogin,
                     login.m_BtnFacebookLogin, login.m_BtnTapLogin, login.m_BtnWxLogin, login.m_BtnQqLogin })
            if (button != null) button.gameObject.SetActive(false);
    }

    private static void Label(Button button, string value)
    {
        if (button == null) return;
        foreach (var text in button.GetComponentsInChildren<Text>(true))
        {
            text.text = value;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 24;
            text.resizeTextMaxSize = 36;
        }
    }
}
