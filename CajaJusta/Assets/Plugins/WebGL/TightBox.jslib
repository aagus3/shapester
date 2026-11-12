mergeInto(LibraryManager.library, {
  // Shows the browser's text prompt (brings up the phone keyboard). Returns "" if cancelled.
  TB_Prompt: function (titlePtr, defaultPtr) {
    var r = window.prompt(UTF8ToString(titlePtr), UTF8ToString(defaultPtr));
    if (r === null || r === undefined) r = "";
    var size = lengthBytesUTF8(r) + 1;
    var buf = _malloc(size);
    stringToUTF8(r, buf, size);
    return buf;
  }
});
