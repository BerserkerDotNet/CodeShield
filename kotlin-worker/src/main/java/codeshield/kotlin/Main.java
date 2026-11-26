package codeshield.kotlin;

import java.io.PrintStream;

/**
 * Process entry point. It is written in Java because the Kotlin runtime itself comes from the user's detekt-cli: when that
 * installation is missing or incompatible, the failure must still be reported with the "missing analyzer" exit code.
 */
public final class Main {
    /** The adapter maps this exit code to MISSING_ANALYZER and shows stderr to the user. */
    static final int MISSING_ANALYZER = 3;

    private Main() {
    }

    public static void main(String[] args) {
        PrintStream stdout = System.out;
        // Analyzer libraries must not corrupt the JSON response, so anything they print goes to stderr.
        System.setOut(System.err);
        int code;
        try {
            byte[] response = Worker.respond(System.in.readAllBytes());
            stdout.write(response, 0, response.length);
            stdout.flush();
            code = stdout.checkError() ? 1 : 0;
        } catch (LinkageError error) {
            System.err.println("The Kotlin worker could not use the detekt-cli installation (" + error
                + "). detekt-cli 1.23.x is required; install it or set CODESHIELD_DETEKT.");
            code = MISSING_ANALYZER;
        } catch (Throwable error) {
            error.printStackTrace();
            code = 1;
        }
        System.exit(code);
    }
}
