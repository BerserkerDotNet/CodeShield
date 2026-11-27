package codeshield.kotlin

/** Decodes one request, runs the command and encodes the response (see docs/kotlin-worker-protocol.md). */
object Worker {
    @JvmStatic
    fun respond(input: ByteArray): ByteArray {
        val request = Json.parse(String(input, Charsets.UTF_8).removePrefix("\uFEFF")) as? Map<*, *>
            ?: throw IllegalArgumentException("The request must be a JSON object.")
        val protocol = request["protocol"]
        require(protocol is Number && protocol.toDouble() == PROTOCOL_VERSION.toDouble()) { "Unsupported protocol $protocol" }
        val response: Any = when (val command = request["command"]) {
            "version" -> Versions.all()
            "tokenize" -> Tokenizer.tokenize(TokenizeRequest(root(request), strings(request, "files", required = true)))
            "analyze" -> Analyzer.analyze(analyzeRequest(request))
            else -> throw IllegalArgumentException("Unknown command $command")
        }
        return Json.write(response).toByteArray(Charsets.UTF_8)
    }

    private fun analyzeRequest(request: Map<*, *>): AnalyzeRequest = AnalyzeRequest(
        root = root(request),
        files = strings(request, "files", required = true),
        focusFiles = strings(request, "focusFiles", required = false),
        changedConfigFiles = strings(request, "changedConfigFiles", required = false),
        isBaseline = request["isBaseline"] == true,
        correctnessRules = (request["correctnessRules"] as? List<*>).orEmpty().map { rule ->
            val map = rule as? Map<*, *> ?: throw IllegalArgumentException("correctnessRules entries must be objects.")
            CorrectnessRuleRequest(
                analyzerId = map["analyzerId"] as? String ?: throw IllegalArgumentException("A correctness rule requires analyzerId."),
                evaluatorId = map["evaluatorId"] as? String ?: throw IllegalArgumentException("A correctness rule requires evaluatorId."),
                options = (map["options"] as? Map<*, *>).orEmpty().entries.associate { (key, value) -> key as String to value },
            )
        },
    )

    private fun root(request: Map<*, *>): String =
        request["root"] as? String ?: throw IllegalArgumentException("Request requires root and files.")

    private fun strings(request: Map<*, *>, name: String, required: Boolean): List<String> {
        val value = request[name] ?: if (required) throw IllegalArgumentException("Request requires root and files.") else return emptyList()
        val list = value as? List<*> ?: throw IllegalArgumentException("$name must be an array of strings.")
        return list.map { it as? String ?: throw IllegalArgumentException("$name must be an array of strings.") }
    }
}
