# Open Beta (v0.8)

""" The stable socket protocol:
        Data and Call. These are headers on every message of the server or client
        Data means to read it's content as raw data, be it a confirmation, a random string, or a game_update
        Data is useful in the context of where it's read

        Call means to read it's contents as an order from either the client or the server, if permission is allowed.
        An example for a call from the client would be leaving the match.
        An example for a call from the host would be closing the server or kicking a player.
        An example for a call from the server would be initiating a game.

        Data and Call are used as headers in a message. An example for a Call message would be CALL:START_GAME

        This protocol is bypassed in the token verification. """


import socket
import threading
import secrets
from time import sleep
import ctypes
import random
import traceback

try:
    ctypes.windll.kernel32.SetConsoleTitleW("Minesweeper host console")
except Exception:
    pass


MINIGAME_NAME_ALIASES = {200: "CO-OP", 202: "Battle Royale"}

# A dictionary containing each addr with the last message it posted
postBoard = {}

INITIAL_HOST_PORT = 22222
LOCAL_HOST_IP = "127.0.0.1"

CONN_BUFFER = 2048

# A token that proves the host
host_token = ""

# A tuple containing the endpoint of the host along with it's conn object
host_ID = (None, None)

# A bool that keeps note if the game has started
game_running = False

# A flag for if the server is attempting to close/is closing
server_closing = False

# Propeties format goes like this: HostingIp \n HostingPort \n GameCreationPage \n GameWidth \n GameHeight \n GameMines ...
# Note that there should not be any spaces between each \n. The spaces are for this comment's readabillity only!
# After this initial "Path" the special propeties are sent and used as arguments in this script. Each "Special" propety is also seprated by a \n
# \n is the general seperator used in this string.
rawGamePropeties = ""

# This list contains 3 raw strings representing the host's gameplane
raw_coop_shared_game_plane = []
# A list of the 3 raw strings in dictionary form
modern_coop_shared_game_plane = []

# A dictionary of {thread: addr} used for managing clients
clientIDs = {}

# A dictionary of addr and conn
clientConns = {}

# A dictionary that keeps track if all of the clients are game ready
client_game_ready = {}

# A dictionary containing one cycle of the votes that were submitted by each client. Key is addr, value is vote
# vote is a tuple consisting of the action and the tileNumber
coop_votes = {}

coop_chosen_vote = None

# A dict that stores each addr along with it's place in the cycle
coop_player_game_cycle = {}

coop_game_cycle = 0

coop_semi_console_message = "null"

# method is string saying what method to pick the votes from. methods: vote , random
def coop_pick_vote(method):
    global coop_votes

    if (method == "Vote"):
        if (len(coop_votes.values()) != len(set(coop_votes.values()))):
            return max(set(coop_votes.values()), key=list(coop_votes.values()).count)
        else:
            return random.choice(list(set(coop_votes.values())))
    elif (method == "Random"):
        return random.choice(list(set(coop_votes.values())))
    else:
        raise NotImplementedError()

# Gets key of a value in a dictionary, only works in dictioaries where keys are unique!
# Returns None if the given value does not exist in the dictionary
def get_key(dict_to_scan, val):
    if val in dict_to_scan.values():
        return [key for (key, value) in dict_to_scan.items() if value == val][0]
    else:
        return None

def get_live_connections():
    global clientIDs
    return [val for val in clientIDs.values() if val != None]

# This function listens to each individual player after a game has been decided.
def GamePlayersListener(stableSocket):
    global rawGamePropeties
    global host_ID
    global postBoard

    print("Server looking for connection at:", rawGamePropeties[0] + ":" + rawGamePropeties[1])
    
    conn, addr = stableSocket.accept()
    
    clientIDs[threading.current_thread()] = addr
    clientConns[addr] = conn
    coop_player_game_cycle[addr] = 0
    
    next_link = threading.Thread(name="Connection thread #{}".format(len(clientIDs) + 1), target=GamePlayersListener, args=(stableSocket,))
    next_link.start()
    clientIDs[next_link] = None
    
    print(" ".join([thread.name for thread in clientIDs.keys()]))
    
    try:
        if (game_running):
            conn.send(b"DATA:GAME_ALREADY_STARTED")
            conn.close()
            del clientIDs[get_key(clientIDs, addr)]
            del coop_player_game_cycle[addr]
            return
        elif host_ID[0] != None:
            conn.send(b"DATA:CONNECTION_ACCEPTED")
    except Exception as error:
        if type(error) == TypeError or type(error) == SyntaxError:
            raise
        del clientIDs[get_key(clientIDs, addr)]
        del coop_player_game_cycle[addr]
        return
        

    print("Got connection from:", addr, "Connection number #" + str(len(clientIDs) + 1))


    with conn:
        try:
            verify_token(conn, addr)
            
            client_game_ready[addr] = False
            
            # Communication loop
            while True:
                if (clientIDs.get(threading.current_thread()) == None):
                    break
                elif (server_closing):
                    disconnect_client(conn, addr)
                
                # Reads client input (2^11)
                clientInput = conn.recv(CONN_BUFFER).decode()
                
                # Some logs
                postBoard[addr] = clientInput

                # Handles the client input and prin
                handle_client_input(clientInput, conn, addr)
                
                # Prints log to console
                print(str(addr) + "<<<", repr(clientInput))
        except Exception as clientException:
            if (type(clientException) == SyntaxError or type(clientException) == TypeError):
                raise
            
            print("An exception has occoured and handled in conn for {0}, isHost={1}:\n{2}".format(addr, addr == host_ID[0], traceback.format_exc()))

            # Connection to the client is lost!
            if (get_key(clientIDs, addr) != None): # Prevents attempting to double disconnect the client
                disconnect_client(conn, addr)
            
            return

# Discoonnects the client cleanly
def disconnect_client(conn, addr):
    try:
        conn.send(b"CALL:QUIT")
    except Exception:
        conn.close()
        
    print(addr, "disconnected!")
    del clientIDs[threading.current_thread()]

# Verifies the token after the conn has been established
def verify_token(conn, addr):
    global host_ID
    
    clientInput = conn.recv(32).decode()

    if (host_token == clientInput and host_ID[0] == None):
        host_ID = (addr, conn)
        conn.send(b'1')
    else:
        conn.send(b'11')
    
# This function makes the initial connection between the host C# application and reads the game propeties from it and returns it in string form
def initialSetup():
    global host_token

    print("Attempting initial connection to C# Host at port:", INITIAL_HOST_PORT)
    while True:
        try:
            initialSocket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            initialSocket.connect((LOCAL_HOST_IP, INITIAL_HOST_PORT))
            print("Connection successful!")
            break
        except OSError:
            initialSocket.close()
            sleep(0.1)
    # atm recives a 2^11 bytes long message aka 512 characters
    clientInput = initialSocket.recv(CONN_BUFFER).decode()
    
    # Might want to do some validation on that string that it's not wrongly formatted, maybe later on the road.
    return clientInput, initialSocket

# This function is the brain of the client input handling and processing. If client input is DATA it'll return it in raw string form.
# If client_input is a CALL it'll execute said CALL according to enviorment variables.
# The function can also respond back to the client UNLESS client_input is DATA
def handle_client_input(message, conn, addr):
    global server_closing
    global game_running
    global coop_votes
    global raw_coop_shared_game_plane
    global modern_coop_shared_game_plane
    global coop_chosen_vote
    global coop_game_cycle
    
    if (server_closing):
        disconnect_client(conn, addr)
        return None

    if game_running and not client_game_ready[addr]:
        conn.send(b"CALL:START_GAME")
        
        if (conn.recv(CONN_BUFFER).decode() == "DATA:STARTING_GAME"):
            client_game_ready[addr] = True
    
    if (coop_player_game_cycle[addr] < coop_game_cycle):
        if (host_ID[0] == addr):
            conn.send(b"CALL:VIRTUAL_CLICK\n" + bytes("\n".join(coop_chosen_vote) + "\n", "utf-8"))

            # Get game plane from plyer
            # lol 2^17
            player_response = conn.recv(131072).decode()
            if (player_response):
                print("&&&" + str(addr), "accepted virtual click, currently on cycle:", coop_player_game_cycle[addr])
            
                coop_player_game_cycle[addr] += 1
                
                raw_coop_shared_game_plane = player_response.split("\n")
                modern_coop_shared_game_plane = modernize_raw_shared_game_plane(raw_coop_shared_game_plane)
                print(raw_coop_shared_game_plane)
                
                print("&&&", str(coop_player_game_cycle) + ",", coop_game_cycle)
                
            return None
        elif (raw_coop_shared_game_plane != []):
            if (coop_game_cycle < 2):
                conn.send(b"CALL:UPDATE_GAME_PLANE\n" + bytes("\n".join(raw_coop_shared_game_plane), "utf-8"))
            else:
                conn.send(b"CALL:VIRTUAL_CLICK\n" + bytes("\n".join(coop_chosen_vote) + "\n", "utf-8"))
                # Discard client response
                conn.recv(4096).decode()
            coop_player_game_cycle[addr] += 1
            return None
    
    if (coop_player_game_cycle[addr] > coop_game_cycle):
        conn.send(b"CALL:RESTART_GAME")
        conn.recv(256).decode()
        
        coop_player_game_cycle[addr] = 0
    
    if (message[:5:] == "CALL:"):
        message = message[5::]
        
        if (message == "UPDATE_PREVIEW"):
            # Returns a string with this format: GamePropeties\nPlayercount
            conn.send(bytes("DATA:" + "\n".join(rawGamePropeties) + "\n" + str(len(get_live_connections())), "utf-8"))

        elif (message == "ONLINE_GAME_TICK"):
            # CO-OP game
            if (rawGamePropeties[2] == str(200)):
                conn.send(b"DATA:TICK\n" + bytes(",".join([value[1] for value in coop_votes.values()]), "utf-8"))

        elif (message == "QUIT"):
            if addr == host_ID[0]:
                server_closing = True
                print("&&&", "Server is now in closing state! Disconnecting all clients!")
            else:
                disconnect_client(conn, addr)
                
        elif (message == "START_GAME"):
            if addr == host_ID[0]:
                conn.send(b"CALL:START_GAME")
                client_response = conn.recv(CONN_BUFFER).decode()
                if (client_response == "DATA:STARTING_GAME"):
                    client_game_ready[addr] = True
                    game_running = True
                else:
                    client_game_ready[addr] = False
            else:
                conn.send(b"DATA:Access Denied")
        
        elif (message.split("\n")[0] == "RESTART_GAME"):
            if addr == host_ID[0] and coop_game_cycle > 0:
                # Sorta a lock so that a game restart wont interrupt a game update
                while (len(set(coop_player_game_cycle.values())) != 1):
                    continue

                # Data nullification
                game_running = False
                coop_game_cycle = 0
                coop_votes = {}
                raw_coop_shared_game_plane = []
                modern_coop_shared_game_plane = []
                
                # Server response
                conn.send(b"DATA:Restart confirmed")
            else:
                conn.send(b"DATA:Acess Denied")
        elif (message.split("\n")[0] == "COOP_VOTE"):
            if (len(coop_votes) < len(clientIDs)):
                if (not(message.split("\n")[1] != "LeftClick" and coop_game_cycle == 0)):
                    if (coop_game_cycle > 0):
                        if (modern_coop_shared_game_plane[1][int(message.split("\n")[2])] ^ (message.split("\n")[1] != "RightAndLeftClick")):
                            coop_vote_brain(addr, conn, message)
                        else:
                            conn.send(b"DATA:Vote not legal")
                    else:
                        coop_vote_brain(addr, conn, message)
                else:
                    conn.send(b"DATA:Vote rejected, only LeftClick allowed in the initial cycle")
            else:
                conn.send(b"DATA:Vote rejected")
        
        else:
            print("!!! BAD DATA FROM {0} || isHost={1}: {2} !!!".format(addr, addr == host_ID[0], message))
            conn.send(b"DATA:NULL")
        
        return None
    
    elif (message[:5:] == "DATA:"):
        return message[5::]
    
    elif (message == ""):
        print("!!! MESSAGE FROM:", addr, "IS EMPTY !!!")
        disconnect_client(conn, addr)

# converts the raw gameplane to it's modern counterpart and returns it
def modernize_raw_shared_game_plane(raw_gameplane):
    output = [{}, {}, {}]
    
    for i in range(3):
        bufferVar = None
        for element in raw_gameplane[i].split(','):
            if (bufferVar == None):
                bufferVar = int(element)
            elif element == "True" or element == "False":
                output[i][bufferVar] = eval(element)
                bufferVar = None
            else:
                output[i][bufferVar] = int(element)
                bufferVar = None
                
    return output

# The brain of the coop_vote server response, should only be used when a vote is accepted.
def coop_vote_brain(addr, conn, message):
    global coop_votes
    global coop_chosen_vote
    global raw_coop_shared_game_plane
    global modern_coop_shared_game_plane
    global coop_game_cycle
    
    coop_votes[addr] = tuple(message.split("\n")[1::])
    print("&&& {0}'s vote was registered: {1}".format(addr, coop_votes[addr]))
    if (len(coop_votes) == len(clientIDs.values()) - list(clientIDs.values()).count(None)):
        chosen_vote = coop_pick_vote(rawGamePropeties[6])
        print("Vote cycle ended! Choosen vote:", chosen_vote)
        coop_votes = {}
        coop_chosen_vote = chosen_vote
        raw_coop_shared_game_plane = []
        modern_coop_shared_game_plane = []
        coop_game_cycle += 1
    conn.send(b"DATA:Vote registered")

if __name__ == "__main__":
    rawGamePropeties, initialSocket = initialSetup()
    rawGamePropeties = rawGamePropeties.split("\n")

    print("rawGamePropeties: " + " ".join(rawGamePropeties))
    stableSocket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        print("Attempting to listen to: ({}, {})".format(rawGamePropeties[0], rawGamePropeties[1]))
        stableSocket.bind((rawGamePropeties[0], int(rawGamePropeties[1])))
        stableSocket.listen(1)
    except:
        print("Could not open socket in the given IP or Port!")
        input()
        raise

    # Only send the token once the script starts listening and expecting an imediate connection
    host_token = secrets.token_urlsafe(16)
    initialSocket.send(host_token.encode())
    initialSocket.close()

    next_link = threading.Thread(name="Connection thread #1", target=GamePlayersListener, args=(stableSocket,))
    next_link.start()
    clientIDs[next_link] = None

    # Some diagnostics
    while ((len(get_live_connections()) > 0 or not(server_closing)) and host_ID[0] in clientIDs.values()):
        print("&&&", clientIDs)
        sleep(5)

stableSocket.close()
print("... Finished! ...\nYou may close this window now...\n\n\n")
exit()