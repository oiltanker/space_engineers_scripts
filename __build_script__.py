import sys
from os import path
import re
from pyperclip import copy # dead project but oh well, tkinter has lazy clipboard bug

TYPE_DEF_FILE = 'lib/__type_defs__.cs'
type_defs = dict()
alreadyImported = []

def findImport(root, fileName):
    filePath = "{}/{}.cs".format(root, fileName.replace('.', '/'))
    return path.isfile(filePath), filePath

def processTypeDefs(line):
    while True:
        match = re.match(r'^([^"]*"[^"]*"[^"]*)*([^"]*)(@[A-z0-9_]+)', line)
        if match is None: break

        rStr = match.group(3)
        if rStr in type_defs.keys():
            rType = type_defs[rStr]
            line = line[:match.start(3)] + rType + line[match.end(3):]
        else:
            print('ERROR: unknown type definition "{}"'.format(rStr))
            return None
    return line

def buildScript(root, fileName):
    contents = None
    with open(fileName, 'r') as file:
        contents = file.read()

    if contents is None:
        print('ERROR: unable to read "{}" contents'.format(fileName))
        return None
    else: print('contents of "{}" read, processing ...'.format(fileName))

    lines = contents.replace('\r', '').split('\n')
    contents = ''
    for line in lines:
        matchImport = re.match(r'^\s*@import (.*)\s*$', line)
        matchSkip =   re.match(r'^\s*@skip (.*)\s*$',   line)
        if matchImport is not None:
            iName = matchImport.group(1)
            if (iName not in alreadyImported):
                alreadyImported.append(iName)
                isOk, iFile = findImport(root, iName)
                if isOk:
                    iContents = buildScript(root, iFile)
                    if iContents is None: return None
                    contents += iContents
                else:
                    print('ERROR: import "{}" does not exist\n  expected to be {}, located in {})'.format(iName, iFile, root))
                    return None
        elif matchSkip is not None:
            iName = matchImport.group(1)
            if (iName not in alreadyImported): alreadyImported.append(iName)
        else:
            line = processTypeDefs(line)
            if line is not None: contents += line + '\n'
            else: return None

    return contents

def loadTypeDefs(root):
    tdFile = "{}/{}".format(root, TYPE_DEF_FILE)
    if (not path.isfile(tdFile)):
        print("ERROR: type definition file does not exist\n  swould be located in {}".format(tdFile))
        return False

    contents = None
    with open(tdFile, 'r') as file:
        contents = file.read()
    if contents is None:
        print('ERROR: unable to read "{}" contents'.format(tdFile))
        return False
    
    for line in contents.split('\n'):
        terms = line.split(' ')
        type_defs[terms[0]] = terms[1]

    print("Loaded type definitions:\n" + str.join('\n', map(lambda kv: "  {}:  {}".format(kv[0], kv[1]), type_defs.items())))
    return True

def getParam(param, argv):
    param = '--{}='.format(param)
    for arg in argv:
        if arg.startswith(param):
            arg = arg[len(param):].replace('\\', '/')
            if arg.startswith('"'): arg = arg[1:]
            if arg.endswith('"'):   arg = arg[:1]
            return arg
    return None

def main(argv):
    root = getParam('root', argv)
    file = getParam('file', argv)
    if root is None or file is None:
        print('ERROR: missing argument, both "root" and "file" arguments must be present')
        return False
    print("root: {}, file: {}".format(root, file))
    if (not path.isdir(root)):
        print("ERROR: root is not a directory")
        return False
    if not loadTypeDefs(root): return
    if (not file.endswith('.cs')):
        print("ERROR: not a script file")
        return False
    script = buildScript(root, file)
    if script is None: return False
    copy(script)
    print("---- ----\n!!! script built and copied to clipboard !!!\n---- ----")

    return True

if __name__ == "__main__":
    if main(sys.argv[1:]): sys.exit(0)
    else: sys.exit(1)