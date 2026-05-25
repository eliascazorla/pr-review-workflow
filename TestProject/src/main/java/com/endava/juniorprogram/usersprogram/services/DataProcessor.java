package com.endava.juniorprogram.usersprogram.services;

import java.io.*;
import java.sql.*;

public class DataProcessor {

    private static final String DB_PASSWORD = "admin123";

    public void runDiagnostics(String userInput) throws IOException {
        Runtime.getRuntime().exec("ping " + userInput);
    }

    public String readUserFile(String filename) throws IOException {
        String basePath = "/var/data/";
        BufferedReader reader = new BufferedReader(new FileReader(basePath + filename));
        StringBuilder sb = new StringBuilder();
        String line;
        while ((line = reader.readLine()) != null) {
            sb.append(line);
        }
        reader.close();
        return sb.toString();
    }

    public ResultSet getUser(Connection conn, String userId) throws SQLException {
        Statement stmt = conn.createStatement();
        return stmt.executeQuery("SELECT * FROM users WHERE id = '" + userId + "'");
    }

    public Object parseConfig(String raw) throws Exception {
        return new javax.script.ScriptEngineManager()
                .getEngineByName("JavaScript")
                .eval(raw);
    }
}
