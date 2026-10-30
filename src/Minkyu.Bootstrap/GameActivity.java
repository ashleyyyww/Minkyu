package app.minkyu.bootstrap;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.security.MessageDigest;

public final class GameActivity extends Activity {
    private ProgressDialog progress;

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        progress = new ProgressDialog(this);
        progress.setMessage("Getting Muse Dash ready...");
        progress.setIndeterminate(true);
        progress.setCancelable(false);
        progress.show();
        new Thread(() -> {
            try {
                prepareExpansion();
                runOnUiThread(() -> {
                    if (isFinishing() || isDestroyed()) return;
                    progress.dismiss();
                    Intent game = new Intent(getIntent());
                    game.setClassName(getPackageName(), "com.unity3d.player.UnityPlayerActivity");
                    startActivity(game);
                    finish();
                });
            } catch (Exception error) {
                Log.e("Minkyu", "Game setup failed", error);
                runOnUiThread(() -> {
                    if (isFinishing() || isDestroyed()) return;
                    progress.dismiss();
                    new AlertDialog.Builder(this)
                        .setMessage("Couldn't prepare the game files. Open Minkyu and check Game setup.")
                        .setPositiveButton("Close", (dialog, which) -> finish())
                        .setCancelable(false)
                        .show();
                });
            }
        }, "Minkyu expansion").start();
    }

    private void prepareExpansion() throws Exception {
        File directory = getObbDir();
        if (directory == null || (!directory.isDirectory() && !directory.mkdirs()))
            throw new java.io.IOException("OBB directory unavailable");
        File destination = new File(directory, "main.626.com.prpr.musedash.obb");
        String uri = getIntent().getStringExtra("minkyu.expansion");
        String expected = getIntent().getStringExtra("minkyu.expansion.sha256");
        if (uri == null || uri.isEmpty()) {
            if (destination.isFile() && destination.length() > 0) return;
            throw new java.io.IOException("Launch from Minkyu to import the OBB");
        }
        if (!"content://app.minkyu.manager.library/expansion".equals(uri)
                || expected == null || !expected.matches("[a-fA-F0-9]{64}"))
            throw new java.io.IOException("Invalid expansion source");

        SharedPreferences verified = getSharedPreferences("minkyu.expansion", MODE_PRIVATE);
        String signature = expected + ":" + destination.length() + ":" + destination.lastModified();
        if (destination.isFile() && signature.equals(verified.getString("verified", ""))) return;
        if (destination.isFile() && expected.equalsIgnoreCase(hash(destination))) {
            verified.edit().putString("verified", signature).commit();
            return;
        }

        // Unity mounts expansion archives before managed mods initialize.
        File pending = new File(directory, destination.getName() + ".bootstrap-pending");
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            try (InputStream input = getContentResolver().openInputStream(Uri.parse(uri));
                 FileOutputStream output = new FileOutputStream(pending)) {
                if (input == null) throw new java.io.IOException("OBB access unavailable");
                byte[] buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = input.read(buffer)) != -1) {
                    total += count;
                    if (total > 4L * 1024 * 1024 * 1024)
                        throw new java.io.IOException("OBB exceeds size limit");
                    digest.update(buffer, 0, count);
                    output.write(buffer, 0, count);
                }
                if (total == 0 || !expected.equalsIgnoreCase(hex(digest.digest())))
                    throw new java.io.IOException("OBB checksum mismatch");
                output.getFD().sync();
            }
            if (!pending.renameTo(destination))
                throw new java.io.IOException("Couldn't save the OBB");
            signature = expected + ":" + destination.length() + ":" + destination.lastModified();
            verified.edit().putString("verified", signature).commit();
            Log.i("Minkyu", "OBB ready before Unity startup");
        } finally {
            if (pending.exists() && !pending.delete()) Log.w("Minkyu", "Couldn't remove incomplete OBB");
        }
    }

    private static String hash(File file) throws Exception {
        if (file.length() == 0 || file.length() > 4L * 1024 * 1024 * 1024) return "";
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        try (InputStream input = new FileInputStream(file)) {
            byte[] buffer = new byte[128 * 1024];
            int count;
            while ((count = input.read(buffer)) != -1) digest.update(buffer, 0, count);
        }
        return hex(digest.digest());
    }

    private static String hex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) result.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
        return result.toString();
    }

    @Override
    protected void onDestroy() {
        if (progress != null) progress.dismiss();
        super.onDestroy();
    }
}
