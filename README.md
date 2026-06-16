# VirtualOffice
<center>
<b>University of Trento</b><br>
<b>Master's Degree in Information Engineering</b><br>
<b>Course of Immersive Technologies: Final Project</b><br><br>
</center>   

![Copertina](/Assets/Images/imm_off.png)

## Project description
Remote work and distributed team collaboration have become increasingly common. Existing tools such as Zoom, Google Meet, and Microsoft Teams have **limitations when multiple participants need to share and compare visual content simultaneously:** traditional screen sharing is sequential, allowing only one presenter at a time and creating friction during collaborative tasks.  
This project presents **Virtual Office**, a Social Virtual Reality application developed in Unity 6 using the Ubiq networking framework. The system recreates an office environment where up to four remote users can collaborate in real time. Each participant is assigned a personal workstation equipped with a virtual whiteboard, a drawable 2D canvas, and virtual monitors displaying the whiteboards of all other users through Render Textures. **This enables continuous awareness of collaborators’ work without requiring screen-sharing handoffs.**  
The project is designed for **Meta Quest 3** headsets and controllers. Key technical features include a multi-chunk snapshot synchronization protocol for late-joining users, a peer-property-based workstation assignment mechanism that mitigates race conditions through a timed synchronization window, and a visibility management system that dynamically activates only occupied workstations.  
**Informal testing** with up to four simultaneous users validated the system’s functionality and demonstrated an engaging collaborative experience. Compared to commercial platforms such as Meta Horizon Workrooms, Virtual Office is intentionally **simpler and fully open-source**, making it a valuable educational prototype and a foundation for future research on immersive collaborative workspaces.

## Group Contribution Statement
The project was developed by two people working in parallel on a single shared Unity scene. Both team members contributed to all aspects of the project throughout its development. Specific contributions:
- Nicola Cappellaro: lighting setup and ColorPicker UI implementation  
- Riccardo Zannoni: office design and workstation positioning  

## Setup
First of all, install the required software:
- Install [Unity Hub](https://unity.com/download)  
- From Unity Hub, install an Editor: Unity 6 LTS version (6000.0.69f1)
- When installing, add the following modules:
  - Dev tools: Microsoft Visual Studio Community 2022
  - Platforms: Android Build Support (select OpenJDK, Android SDK & NDK Tools)
  - Documentation

## Usage istruction
<u>This section describes the user experience from first launch to active collaboration:</u>  
**Joining the office:** when a user starts the application, they are placed in the room in front of the Ubiq panel.The first user has to create the room through the panel, while the other users have to join the created room using the room code. Once inside the room, only the workstations corresponding to currently connected users are active and visible; the rest of the office furniture is always visible to provide context. A user's workstation consists of a personal whiteboard positioned in front of them and the virtual monitors located above it.  
**Drawing:** a virtual pen is located beside the whiteboard. The user grabs it by pressing the grip trigger on their Quest 3 controller (VR mode) or the right mouse button (flat-screen mode). Once held, moving the pen tip towards the whiteboard surface causes the nib to draw on the whiteboard.  
**Changing color:** a button beside the workstation allows the users to open a floating color picker panel. The panel exposes three sliders (Hue, Saturation, Value) and an optional hex code input field. A color preview swatch shows the current color in real time. An Eraser button sets the color to white. The UI synchronizes automatically with the color of the pen currently in the user's hand.  
**Viewing other participants:** the virtual monitors above each user's whiteboard display the whiteboards of the other connected participants as live Render Textures. They are read-only: users can only draw on their own whiteboard.
Late Join: when a new user joins a room where other participants have already drawn on their whiteboards, the new peer automatically sees all drawings that were created before they entered the room.  



