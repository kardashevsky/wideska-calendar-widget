# Connect your Google Calendar to Wideska

## Start Wideska

Run the portable Windows x64 `Wideska.exe`. The self-contained build includes the .NET runtime; no separate .NET installation is required. The first launch shows the Google connection screen.

Wideska does not bundle a shared Google OAuth client. You need a Google account and your own Google Cloud Desktop OAuth client. Your calendars do not need to be public.

## Create your Google OAuth client

1. Open [Google Cloud Console](https://console.cloud.google.com/), create or select a project, and enable **Google Calendar API** in **APIs & Services > Library**.
2. Open **Google Auth platform** and configure **Branding** and **Audience**. If the application is in testing mode, add the Google account you will connect as a test user. Organization accounts may require administrator approval.
3. In **Clients**, create a client with application type **Desktop app** and download its JSON file. Web app and service account credentials will not work.

## Connect the account

1. In Wideska, select **Import Google OAuth file** and choose the downloaded JSON.
2. Select **Continue with Google** and finish sign-in and consent in your browser. Wideska requests read-only access to Google Calendar. Return to the widget after authorization.
3. Right-click the Wideska tray icon and select **Settings / Google sign-in**.
4. Select the calendars to display and the calendar used for the time zone. Select **Done** to save.

Events synchronize automatically every minute. Create or edit events in Google Calendar; Wideska displays them without modifying your calendars.

## Troubleshooting and account changes

- If access is denied, check that the Calendar API is enabled in the same project as the Desktop OAuth client and that your account is a test user when the project is in testing mode.
- If consent expires or is revoked, reconnect through the tray settings. Testing-mode applications may require repeated authorization.
- If a calendar is missing, verify it is available to the connected Google account and enabled in Wideska settings.
- To change accounts, select **Disconnect Google account**, then connect the new account.

Keep the OAuth JSON private. Do not commit it or upload it to GitHub. Wideska stores imported configuration, tokens and cached events encrypted for your Windows user under `%LOCALAPPDATA%\Wideska`.

## Update Wideska

Choose **Exit Wideska** from the tray before replacing the executable. Your local settings remain available for the next launch.
